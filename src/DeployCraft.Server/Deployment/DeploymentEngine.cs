using System.Collections.Concurrent;
using System.Threading.Channels;
using DeployCraft.Server.Data;
using DeployCraft.Server.Remote;

namespace DeployCraft.Server.Deployment;

/// <summary>
/// File d'attente des déploiements. Chaque PC est traité indépendamment,
/// avec au plus <see cref="Defaults.MaxParallelTargets"/> PCs en parallèle (prioritaires d'abord).
/// </summary>
public class DeploymentEngine(Store store, DeployCredentials credentials, ILogger<DeploymentEngine> log) : BackgroundService
{
    private readonly Channel<long> _queue = Channel.CreateUnbounded<long>();
    private readonly SlotQueue _slots = new(Defaults.MaxParallelTargets);
    private readonly string _runnerExe = Path.Combine(AppContext.BaseDirectory, "runner", "DeployCraft.Runner.exe");

    /// <summary>Postes en attente ou en cours, par id de cible, pour pouvoir les annuler.</summary>
    private readonly ConcurrentDictionary<long, (long DeploymentId, CancellationTokenSource Cts)> _active = new();

    /// <summary>Fin de chaque poste, par id de cible : le paquet suivant sur le même poste l'attend.</summary>
    private readonly ConcurrentDictionary<long, TaskCompletionSource> _done = new();

    /// <summary>Cible du paquet précédent sur le même poste (plusieurs paquets lancés ensemble).</summary>
    private readonly ConcurrentDictionary<long, long> _after = new();

    /// <summary>
    /// Lance un déploiement par paquet, dans l'ordre donné. Sur un même poste, un paquet ne commence
    /// qu'une fois le précédent terminé (même en échec). L'étape de départ s'applique au premier paquet.
    /// </summary>
    public List<Data.Deployment> Start(IReadOnlyList<Package> packages, IEnumerable<TargetRequest> targets, string? launchedBy, DeployOptions options)
    {
        var requests = targets
            .Select(t => t with { Computer = t.Computer.Trim().TrimStart('\\') })
            .Where(t => t.Computer.Length > 0)
            .DistinctBy(t => t.Computer, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (requests.Count == 0) throw new ArgumentException("Aucune cible.");
        if (packages.Count == 0) throw new ArgumentException("Aucun paquet.");
        foreach (var p in packages.Where(p => p.Steps.Count == 0))
            throw new ArgumentException($"Le paquet « {p.Name} » n'a aucune étape.");
        foreach (var t in requests.Where(t => t.StartStep < 1 || t.StartStep > packages[0].Steps.Count))
            throw new ArgumentException($"{t.Computer} : étape de départ invalide ({t.StartStep}) ; le paquet a {packages[0].Steps.Count} étape(s).");

        return StartPlan(
            packages.Select((p, i) => (p, i == 0 ? requests : requests.Select(t => t with { StartStep = 1 }).ToList())).ToList(),
            launchedBy, options, schedule: null);
    }

    /// <summary>
    /// Lance un déploiement par paquet, chacun avec ses propres postes (une planification ne renvoie
    /// pas un paquet déjà réussi). Sur un même poste, les paquets restent enchaînés dans l'ordre.
    /// </summary>
    public List<Data.Deployment> StartPlan(IReadOnlyList<(Package Package, List<TargetRequest> Targets)> plan, string? launchedBy,
        DeployOptions options, Schedule? schedule)
    {
        // Identifiant désigné, sinon celui par défaut ; aucun enregistré = identité du service.
        var credential = credentials.Resolve(options.CredentialId);
        if (options.CredentialId != null && credential == null) throw new ArgumentException("Identifiant introuvable (supprimé ?).");
        var account = credential?.Account ?? "Compte du service";

        var deployments = new List<Data.Deployment>();
        var previous = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        foreach (var (package, packageTargets) in plan.Where(p => p.Targets.Count > 0))
        {
            var runAs = options.RunAs ?? package.RunAs;
            // Sans identifiant, le service ne peut tourner que sous un compte intégré.
            if (runAs == RunAsMode.DeployUser && credential == null) runAs = RunAsMode.LocalSystem;
            var copyMode = options.CopyMode ?? package.CopyMode;

            var deployment = store.CreateDeployment(package, packageTargets, launchedBy, account, credential?.Id, runAs, copyMode, options.Prioritize, schedule);
            foreach (var t in deployment.Targets)
            {
                _done[t.Id] = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                if (previous.TryGetValue(t.Computer, out var before)) _after[t.Id] = before;
                previous[t.Computer] = t.Id;
            }
            deployments.Add(deployment);
        }

        foreach (var d in deployments) _queue.Writer.TryWrite(d.Id);
        return deployments;
    }

    /// <summary>
    /// Annule des postes d'un déploiement (tous si <paramref name="targetIds"/> est vide).
    /// Un poste en attente n'est pas lancé ; un poste en cours voit son installation arrêtée.
    /// </summary>
    public int Abort(long deploymentId, IReadOnlyCollection<long>? targetIds, string? by)
    {
        var count = 0;
        foreach (var (targetId, entry) in _active)
        {
            if (entry.DeploymentId != deploymentId) continue;
            if (targetIds is { Count: > 0 } && !targetIds.Contains(targetId)) continue;
            _abortedBy[targetId] = by;
            entry.Cts.Cancel();
            count++;
        }
        return count;
    }

    private readonly ConcurrentDictionary<long, string?> _abortedBy = new();

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        store.FailInterrupted();
        if (!File.Exists(_runnerExe)) log.LogError("Runner introuvable : {Path}", _runnerExe);

        await foreach (var id in _queue.Reader.ReadAllAsync(ct))
        {
            // Chaque déploiement avance en parallèle des autres ; la file de créneaux limite le total de PCs.
            _ = Task.Run(() => RunDeploymentAsync(id, ct), ct);
        }
    }

    private async Task RunDeploymentAsync(long id, CancellationToken ct)
    {
        var deployment = store.GetDeployment(id);
        if (deployment == null) return;

        // Enregistrés tout de suite : un poste encore en attente doit pouvoir être annulé.
        var runs = deployment.Targets.Select(t =>
        {
            var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            _active[t.Id] = (id, cts);
            return (Target: t, Cts: cts);
        }).ToList();

        store.UpdateDeploymentStatus(id, DeploymentStatus.Running, ended: false);
        await Task.WhenAll(runs.Select(r => RunTargetAsync(deployment, r.Target, r.Cts, ct)));

        var ok = deployment.Targets.All(t => t.Status == TargetStatus.Successful);
        store.UpdateDeploymentStatus(id, ok ? DeploymentStatus.Successful : DeploymentStatus.Failed, ended: true);
    }

    private async Task RunTargetAsync(Data.Deployment deployment, DeploymentTarget target, CancellationTokenSource cts, CancellationToken appStopping)
    {
        var slotTaken = false;
        // Compté dans l'historique de la planification : pas les annulations ni les postes jamais démarrés.
        var countForSchedule = false;
        // Attente d'un créneau limitée (option de planification) : le poste est abandonné s'il n'a pas démarré à temps.
        using var waitCts = CancellationTokenSource.CreateLinkedTokenSource(cts.Token);
        if (deployment.QueueTimeoutMinutes is > 0 and var minutes)
        {
            var left = TimeSpan.FromMinutes(minutes) - (DateTime.UtcNow - deployment.CreatedUtc);
            waitCts.CancelAfter(left > TimeSpan.Zero ? left : TimeSpan.Zero);
        }
        try
        {
            try
            {
                if (_after.TryRemove(target.Id, out var before) && _done.TryGetValue(before, out var previous))
                {
                    target.Stage = "En attente du paquet précédent";
                    store.UpdateTarget(target);
                    await previous.Task.WaitAsync(waitCts.Token);
                }

                await _slots.WaitAsync(target.Prioritized, waitCts.Token);
                slotTaken = true;
            }
            catch (OperationCanceledException) when (waitCts.IsCancellationRequested && !cts.IsCancellationRequested)
            {
                target.Status = TargetStatus.Failed;
                target.Error = $"Pas démarré : toujours en attente après {deployment.QueueTimeoutMinutes} min (délai de la planification).";
                return;
            }
            countForSchedule = true;

            target.Status = TargetStatus.Running;
            target.StartedUtc = DateTime.UtcNow;
            target.Stage = "Démarrage";
            store.UpdateTarget(target);

            Credential? credential = null;
            if (deployment.CredentialId is { } credentialId)
                credential = credentials.Get(credentialId)
                    ?? throw new DeployException($"L'identifiant {deployment.RunAs} a été supprimé depuis le lancement.");
            var session = new TargetSession(target.Computer, credentials, credential, _runnerExe);
            await session.RunAsync(
                deployment.Snapshot,
                target.StartStep,
                deployment.RunAsMode,
                deployment.CopyMode,
                stage: s => { target.Stage = s; store.UpdateTarget(target); },
                progress: status => { target.Steps = ToOutcomes(status); store.UpdateTarget(target); },
                cts.Token);

            var failed = target.Steps.FirstOrDefault(s => s.State == "failed");
            target.Status = failed == null && target.Steps.Count > 0 ? TargetStatus.Successful : TargetStatus.Failed;
            if (failed != null) target.Error = ErrorMessages.ForStep(failed);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested && !appStopping.IsCancellationRequested)
        {
            countForSchedule = false;
            target.Status = TargetStatus.Failed;
            _abortedBy.TryRemove(target.Id, out var by);
            target.Error = string.IsNullOrEmpty(by) ? "Annulé." : $"Annulé par {by}.";
        }
        catch (Exception ex)
        {
            target.Status = TargetStatus.Failed;
            target.Error = ErrorMessages.ForException(ex);
            log.LogWarning(ex, "Échec sur {Computer}", target.Computer);
        }
        finally
        {
            target.Stage = null;
            target.StartedUtc ??= DateTime.UtcNow;
            target.EndedUtc = DateTime.UtcNow;
            store.UpdateTarget(target);
            if (countForSchedule && deployment.ScheduleId is { } scheduleId && !appStopping.IsCancellationRequested)
            {
                try
                {
                    store.RecordScheduleResult(scheduleId, target.Computer, deployment.PackageId, target.Status == TargetStatus.Successful);
                }
                catch (Exception ex) { log.LogWarning(ex, "Historique de planification non mis à jour ({Computer})", target.Computer); }
            }
            if (slotTaken) _slots.Release();
            _active.TryRemove(target.Id, out _);
            cts.Dispose();
            if (_done.TryRemove(target.Id, out var done)) done.TrySetResult();
        }
    }

    private static List<StepOutcome> ToOutcomes(RunnerStatus status) =>
        status.Steps.Select(s => new StepOutcome
        {
            Name = s.Name,
            State = s.State,
            ExitCode = s.ExitCode,
            CommandLine = s.CommandLine,
            Output = s.Output,
            Error = s.Error,
            StartedUtc = s.StartedUtc?.ToUniversalTime(),
            EndedUtc = s.EndedUtc?.ToUniversalTime(),
        }).ToList();
}

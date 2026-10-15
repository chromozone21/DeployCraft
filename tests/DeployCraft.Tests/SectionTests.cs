using DeployCraft.Runner;
using DeployCraft.Server.Data;
using DeployCraft.Server.Deployment;
using DeployCraft.Server.Remote;

namespace DeployCraft.Tests;

/// <summary>Sections Avant / Principales / Après : ordre, « Après » exécutée malgré un arrêt, reprise à l'étape N.</summary>
public class SectionTests
{
    private sealed class Facts : ITargetFacts
    {
        public string WindowsFamily => "11";
        public bool Is64Bit => true;
        public int PowerShellVersion => 5;
        public bool UserLoggedOn => false;
        public bool PathExists(string path) => false;
        public bool RegistryExists(string key, string value) => false;
    }

    private static JobStep Before(string name, bool stopSuccess = false) => new() { Name = name, Section = "before", StopSuccessOnError = stopSuccess };
    private static JobStep Principal(string name, bool stopSuccess = false) => new() { Name = name, Section = "main", StopSuccessOnError = stopSuccess };
    private static JobStep After(string name, bool continueOnError = false) => new() { Name = name, Section = "after", ContinueOnError = continueOnError };

    /// <summary>Exécute le job : les étapes dont le nom commence par « ko » échouent.</summary>
    private static (string[] States, List<string> Ran, RunStatus Status) Run(Job job, Func<bool>? aborted = null)
    {
        var ran = new List<string>();
        var status = new RunStatus();
        StepSequence.Run(job, new Facts(), status,
            (_, step, result) =>
            {
                ran.Add(step.Name);
                result.ExitCode = step.Name.StartsWith("ko") ? 1 : 0;
                result.State = result.ExitCode == 0 ? StepSequence.Success : StepSequence.Failed;
            },
            () => { }, aborted ?? (() => false), _ => { });
        return (status.Steps.Select(s => s.State).ToArray(), ran, status);
    }

    private static Job Job(params JobStep[] steps) => new() { Steps = [.. steps] };

    private static (TargetStatus, string?) Outcome(RunStatus status) =>
        DeploymentEngine.Outcome([.. status.Steps.Select(s => new StepOutcome { Name = s.Name, State = s.State, Error = s.Error, ExitCode = s.ExitCode })]);

    [Fact]
    public void After_RunsEvenWhenAMainStepFails_AndTheComputerStaysFailed()
    {
        var (states, ran, status) = Run(Job(Principal("ko"), Principal("b"), After("nettoyage")));

        Assert.Equal(["ko", "nettoyage"], ran);
        Assert.Equal(["failed", "skipped", "success"], states);
        Assert.Equal(TargetStatus.Failed, Outcome(status).Item1);
    }

    [Fact]
    public void After_RunsAfterAStopWithSuccess_AndTheComputerIsSuccessful()
    {
        var (states, ran, status) = Run(Job(Principal("ko", stopSuccess: true), Principal("b"), After("nettoyage")));

        Assert.Equal(["ko", "nettoyage"], ran);
        Assert.Equal(["stopped", "skipped", "success"], states);
        Assert.Equal(TargetStatus.Successful, Outcome(status).Item1);
    }

    [Fact]
    public void BeforeFailure_SkipsTheMainSteps_ButNotAfter()
    {
        var (_, ran, _) = Run(Job(Before("ko"), Principal("b"), After("nettoyage")));

        Assert.Equal(["ko", "nettoyage"], ran);
    }

    [Fact]
    public void ErrorRules_ApplyInsideAfter_AndAnAfterFailureFailsTheComputer()
    {
        var (stopStates, stopRan, stopStatus) = Run(Job(Principal("a"), After("ko1"), After("c")));
        Assert.Equal(["a", "ko1"], stopRan);
        Assert.Equal(["success", "failed", "skipped"], stopStates);
        Assert.Equal(TargetStatus.Failed, Outcome(stopStatus).Item1);

        var (_, continueRan, _) = Run(Job(Principal("a"), After("ko1", continueOnError: true), After("c")));
        Assert.Equal(["a", "ko1", "c"], continueRan);
    }

    [Fact]
    public void Abort_RunsNothing_AfterIncluded()
    {
        var (states, ran, _) = Run(Job(Principal("a"), After("nettoyage")), aborted: () => true);

        Assert.Empty(ran);
        Assert.Equal(["skipped", "skipped"], states);
    }

    [Fact]
    public void UnmetPackageConditions_SkipEverything_AfterIncluded()
    {
        var job = Job(Principal("a"), After("nettoyage"));
        job.Conditions = new JobConditions { Windows = ["10"] };

        var (states, ran, status) = Run(job);

        Assert.Empty(ran);
        Assert.Equal(["skipped", "skipped"], states);
        Assert.Equal(TargetStatus.Successful, Outcome(status).Item1);
    }

    private static PackageStep Step(string name, StepSection section) => new() { Name = name, Section = section };

    [Fact]
    public void Steps_AreSortedBySection_KeepingTheirOrderInsideEachSection()
    {
        var package = new Package
        {
            Steps = [Step("p1", StepSection.Main), Step("a1", StepSection.After), Step("b1", StepSection.Before),
                     Step("p2", StepSection.Main), Step("b2", StepSection.Before)],
        };

        package.SortSteps();

        Assert.Equal(["b1", "b2", "p1", "p2", "a1"], package.Steps.Select(s => s.Name));
    }

    [Fact]
    public void RestartAtStepN_StillSendsTheAfterSection()
    {
        var package = new Package { Steps = [Step("b1", StepSection.Before), Step("p1", StepSection.Main), Step("p2", StepSection.Main), Step("a1", StepSection.After)] };

        Assert.Equal(["p2", "a1"], TargetSession.StepsFrom(package, 3).Select(s => s.Name));
        Assert.Equal("after", TargetSession.ToJobStep(package.Steps[3], package, RunAsMode.DeployUser).Section);
        Assert.Equal("before", TargetSession.ToJobStep(package.Steps[0], package, RunAsMode.DeployUser).Section);
    }

    [Fact]
    public void OldSteps_AreMainSteps()
    {
        var step = System.Text.Json.JsonSerializer.Deserialize<PackageStep>("""{"name":"a"}""", Store.Json)!;

        Assert.Equal(StepSection.Main, step.Section);
    }
}

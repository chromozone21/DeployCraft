namespace DeployCraft.Server.Deployment;

/// <summary>
/// Limite le nombre de postes traités en même temps, comme un sémaphore,
/// mais les postes prioritaires en attente sont servis avant les autres.
/// </summary>
public sealed class SlotQueue(int slots)
{
    private readonly object _lock = new();
    private readonly LinkedList<TaskCompletionSource> _priority = new();
    private readonly LinkedList<TaskCompletionSource> _normal = new();
    private int _free = slots;

    public async Task WaitAsync(bool prioritized, CancellationToken ct)
    {
        TaskCompletionSource tcs;
        LinkedListNode<TaskCompletionSource> node;
        lock (_lock)
        {
            if (_free > 0 && (prioritized ? _priority.Count == 0 : _priority.Count == 0 && _normal.Count == 0))
            {
                _free--;
                return;
            }
            tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            node = (prioritized ? _priority : _normal).AddLast(tcs);
        }

        await using (ct.Register(() =>
        {
            lock (_lock)
            {
                // Pas encore servi : on quitte la file. Déjà servi : le créneau est à nous, l'appelant le rendra.
                if (node.List == null) return;
                node.List.Remove(node);
            }
            tcs.TrySetCanceled(ct);
        }))
        {
            await tcs.Task;
        }
    }

    public void Release()
    {
        TaskCompletionSource? next = null;
        lock (_lock)
        {
            var list = _priority.Count > 0 ? _priority : _normal.Count > 0 ? _normal : null;
            if (list == null)
            {
                _free++;
                return;
            }
            next = list.First!.Value;
            list.RemoveFirst();
        }
        next.TrySetResult();
    }
}

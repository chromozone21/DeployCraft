using DeployCraft.Server.Deployment;

namespace DeployCraft.Tests;

public class SlotQueueTests
{
    [Fact]
    public async Task PrioritizedWaiter_IsServedBeforeEarlierNormalWaiter()
    {
        var slots = new SlotQueue(1);
        await slots.WaitAsync(prioritized: false, CancellationToken.None);

        var normal = slots.WaitAsync(prioritized: false, CancellationToken.None);
        var priority = slots.WaitAsync(prioritized: true, CancellationToken.None);
        slots.Release();

        await priority.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(normal.IsCompleted);

        slots.Release();
        await normal.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task CancelledWaiter_LeavesTheQueueWithoutTakingASlot()
    {
        var slots = new SlotQueue(1);
        await slots.WaitAsync(prioritized: false, CancellationToken.None);

        using var cts = new CancellationTokenSource();
        var cancelled = slots.WaitAsync(prioritized: false, cts.Token);
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled);

        slots.Release();
        // Le créneau rendu est de nouveau libre : un nouveau venu passe tout de suite.
        await slots.WaitAsync(prioritized: false, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
    }
}

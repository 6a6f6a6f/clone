using Clone.Git;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Clone.Tests;

[TestClass]
public sealed class OutputDeliveryTests
{
    [TestMethod]
    public async Task SaturationKeepsRecentMessagesAndCoalescesDrops()
    {
        using var sink = new BlockingWriter();
        var messages = new List<string>();
        var delivery = new OutputDelivery((line, _) =>
        {
            if (line == "first") sink.WriteLine(line);
            lock (messages) messages.Add(line);
        });
        delivery.Write("first", false);
        try
        {
            await sink.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            for (var index = 0; index < OutputDelivery.QueueCapacity * 2; index++) delivery.Write(index.ToString(), true);
        }
        finally { sink.Release(); }
        await delivery.CompleteAsync();
        Assert.AreEqual(OutputDelivery.QueueCapacity + 2, messages.Count);
        Assert.AreEqual((OutputDelivery.QueueCapacity * 2 - 1).ToString(), messages[^1]);
        Assert.AreEqual(1, messages.Count(line => line.Contains("omitted")));
    }

    [TestMethod]
    public async Task AbandonedWorkerDoesNotDeliverQueuedMessagesAndObservesLateFailure()
    {
        using var sink = new BlockingWriter();
        var calls = 0;
        var delivery = new OutputDelivery((_, _) =>
        {
            Interlocked.Increment(ref calls);
            sink.WriteLine("blocked");
            throw new IOException("late failure");
        });
        delivery.Write("first", true);
        try
        {
            await sink.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            delivery.Write("queued", true);
            await delivery.CompleteAsync().WaitAsync(TimeSpan.FromSeconds(5));
            delivery.Write("after abandonment", true);
        }
        finally { sink.Release(); }
        await delivery.Failure.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.AreEqual(1, calls);
    }
}

internal sealed class BlockingWriter : TextWriter
{
    private readonly ManualResetEventSlim release = new();
    internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal TaskCompletionSource Exited { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal bool Released => release.IsSet;
    public override System.Text.Encoding Encoding => System.Text.Encoding.UTF8;
    public override void WriteLine(string? value)
    {
        Entered.TrySetResult();
        try { release.Wait(); }
        finally { Exited.TrySetResult(); }
    }
    internal void Release() => release.Set();
    protected override void Dispose(bool disposing)
    {
        // Tests release and await Exited before disposing a handle used by a detached worker.
        if (disposing) release.Dispose();
        base.Dispose(disposing);
    }
}

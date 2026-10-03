using System.Threading.Channels;

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("clone")]
[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Clone.Tests")]

namespace Clone.Git;

/// <summary>
/// Delivers bounded, best-effort output without allowing a synchronous sink to block producers.
/// A blocked in-flight callback may outlive completion on its background thread.
/// </summary>
internal sealed class OutputDelivery
{
    internal const int QueueCapacity = 64;
    internal const int MaximumMessageCharacters = 8192;
    internal static readonly TimeSpan ShutdownGrace = TimeSpan.FromMilliseconds(250);
    private const string DroppedMessage = "[Git output omitted because the output consumer was slow]";
    private readonly Channel<(string Text, bool Error)> channel;
    private readonly Action<string, bool> output;
    private readonly TaskCompletionSource completed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource failure = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int stopped;
    private int dropped;

    internal OutputDelivery(Action<string, bool> output)
    {
        this.output = output;
        channel = Channel.CreateBounded<(string, bool)>(new BoundedChannelOptions(QueueCapacity)
        {
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false,
            FullMode = BoundedChannelFullMode.DropOldest
        }, _ => Interlocked.Exchange(ref dropped, 1));
        new Thread(Pump) { IsBackground = true, Name = "Clone output delivery" }.Start();
    }

    internal Task Failure => failure.Task;

    internal void Write(string text, bool error)
    {
        if (Volatile.Read(ref stopped) != 0) return;
        if (text.Length > MaximumMessageCharacters) text = text[..MaximumMessageCharacters];
        channel.Writer.TryWrite((text, error));
    }

    internal void WriteError(string text, bool _) => Write(text, true);

    internal async Task CompleteAsync()
    {
        channel.Writer.TryComplete();
        if (await Task.WhenAny(completed.Task, Task.Delay(ShutdownGrace)).ConfigureAwait(false) != completed.Task)
            Interlocked.Exchange(ref stopped, 1);
        // The worker owns the reader until it exits. Do not dispose resources it may still use.
    }

    private void Pump()
    {
        try
        {
            while (channel.Reader.WaitToReadAsync().AsTask().GetAwaiter().GetResult())
            {
                while (Volatile.Read(ref stopped) == 0 && channel.Reader.TryRead(out var message))
                {
                    if (Interlocked.Exchange(ref dropped, 0) != 0) output(DroppedMessage, true);
                    if (Volatile.Read(ref stopped) != 0) break;
                    output(message.Text, message.Error);
                }
                if (Volatile.Read(ref stopped) != 0) break;
            }
        }
        catch
        {
            Interlocked.Exchange(ref stopped, 1);
            channel.Writer.TryComplete();
            failure.TrySetResult();
        }
        finally
        {
            while (channel.Reader.TryRead(out _)) { }
            completed.TrySetResult();
        }
    }
}

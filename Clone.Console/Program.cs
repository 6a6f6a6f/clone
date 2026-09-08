using Clone.CommandLine;

using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) => { eventArgs.Cancel = true; cancellation.Cancel(); };
return await CliApplication.RunAsync(args, Console.Out, Console.Error, !Console.IsInputRedirected, cancellation.Token);

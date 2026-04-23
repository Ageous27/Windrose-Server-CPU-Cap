using CpuGuard.Service.Configuration;
using CpuGuard.Service.Runtime;
using CpuGuard.Service.Service;

var configPath = ResolveConfigPath(args);
var options = CpuGuardOptions.Load(configPath);

if (Environment.UserInteractive || args.Contains("--console", StringComparer.OrdinalIgnoreCase))
{
    await RunConsoleAsync(options);
    return;
}

RunService(options);

static async Task RunConsoleAsync(CpuGuardOptions options)
{
    using var runtime = new CpuGuardRuntime(options, interactiveConsole: true);
    using var cts = new CancellationTokenSource();

    Console.CancelKeyPress += (_, eventArgs) =>
    {
        eventArgs.Cancel = true;
        cts.Cancel();
    };

    await runtime.RunAsync(cts.Token).ConfigureAwait(false);
}

static void RunService(CpuGuardOptions options)
{
    using var runtime = new CpuGuardRuntime(options);
    NativeServiceHost.Run(options.ServiceName, runtime.RunAsync);
}

static string ResolveConfigPath(IReadOnlyList<string> args)
{
    for (var i = 0; i < args.Count - 1; i++)
    {
        if (string.Equals(args[i], "--config", StringComparison.OrdinalIgnoreCase))
        {
            return args[i + 1];
        }
    }

    return Path.Combine(AppContext.BaseDirectory, "appsettings.json");
}

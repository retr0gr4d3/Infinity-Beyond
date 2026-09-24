using Avalonia;
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace Launcher;

internal sealed class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        StartFileLog();
        BuildAvaloniaApp()
        .StartWithClassicDesktopLifetime(args);
    }

    // Every Trace.WriteLine in the launcher (game patching, agent copy, input
    // focus routing, Avalonia warnings via LogToTrace) goes to
    // UserData/launcher.log; the previous run is kept as launcher.prev.log.
    // Pairs with the agent's UserData/Beyond/logs in the game folder.
    private static void StartFileLog()
    {
        try
        {
            string dir = Path.Combine(AppContext.BaseDirectory, "UserData");
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, "launcher.log");
            if (File.Exists(path))
            {
                File.Copy(path, Path.Combine(dir, "launcher.prev.log"), true);
            }

            StreamWriter writer = new(new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite)) { AutoFlush = true };
            Trace.Listeners.Add(new TimestampedListener(writer));
            Trace.AutoFlush = true;
            Trace.WriteLine($"Beyond launcher {typeof(Program).Assembly.GetName().Version} on {RuntimeInformation.OSDescription} ({RuntimeInformation.ProcessArchitecture}), .NET {Environment.Version}, from '{AppContext.BaseDirectory}'");
        }
        catch
        {
            // No log file is not a reason to not start.
        }
    }

    private sealed class TimestampedListener(TextWriter writer) : TextWriterTraceListener(writer)
    {
        public override void WriteLine(string? message)
        {
            base.WriteLine($"{DateTime.Now:HH:mm:ss.fff} {message}");
        }
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<App>()
                .UsePlatformDetect()
#if DEBUG
                .WithDeveloperTools()
#endif
                .WithInterFont()
                .LogToTrace();
    }
}

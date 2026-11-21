using System.Runtime.Versioning;
using Xueqing.Linux.NativeSpike;
using Xueqing.Windows.ViewModels;

if (!OperatingSystem.IsLinux())
{
    Console.Error.WriteLine("Xueqing.Linux.NativeSpike only runs on Linux.");
    return 1;
}

return await RunLinuxAsync(args);

[SupportedOSPlatform("linux")]
static async Task<int> RunLinuxAsync(string[] args)
{
    if (args.Contains("--security-check", StringComparer.Ordinal))
    {
        try
        {
            var report = await LinuxSecuritySelfTest.RunAsync();
            foreach (var line in report.Checks)
            {
                Console.WriteLine(line);
            }

            Console.WriteLine($"ALL SECURITY CHECKS PASSED (SQLite3MC={report.Sqlite3McVersion}, boot_id={report.BootSessionId}).");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"SECURITY CHECK FAILED: {ex.GetType().Name}: {ex.Message}");
            return 1;
        }
    }

    var smokeGui = args.Contains("--smoke-gui", StringComparer.Ordinal);
    var appId = smokeGui ? "com.xueqing.linux.spike.smoke" : "com.xueqing.linux.spike";
    var app = Adw.Application.New(appId, Gio.ApplicationFlags.NonUnique);

    var smokePassed = false;
    string? smokeFailure = null;

    app.OnActivate += (_, _) =>
    {
        var viewModel = new MainWindowViewModel();
        var window = new LinuxNativeSpikeWindow(app, viewModel);
        window.Present();

        if (smokeGui)
        {
            _ = RunGuiSmokeAsync(app, viewModel, window, passed => smokePassed = passed, err => smokeFailure = err);
        }
    };

    var exitCode = app.RunWithSynchronizationContext(Array.Empty<string>());
    if (smokeGui)
    {
        if (!smokePassed)
        {
            Console.Error.WriteLine($"GUI SMOKE FAILED: {smokeFailure ?? "unknown error"}");
            return 1;
        }

        Console.WriteLine("GUI SMOKE PASSED: Adw.ApplicationWindow + MainWindowViewModel + GLib SynchronizationContext verified.");
    }

    return exitCode;
}

[SupportedOSPlatform("linux")]
static async Task RunGuiSmokeAsync(
    Adw.Application app,
    MainWindowViewModel viewModel,
    LinuxNativeSpikeWindow window,
    Action<bool> setPassed,
    Action<string> setFailure)
{
    try
    {
        var mainThreadId = Environment.CurrentManagedThreadId;
        var initialCount = viewModel.Students.Count;
        if (initialCount != 1_000)
        {
            throw new InvalidOperationException($"Expected 1000 prototype students, got {initialCount}.");
        }

        // Verify await resumes on the GTK/GLib main thread via RunWithSynchronizationContext
        await Task.Delay(120);
        if (Environment.CurrentManagedThreadId != mainThreadId)
        {
            throw new InvalidOperationException(
                $"GLib SynchronizationContext did not marshal back to main thread (expected {mainThreadId}, got {Environment.CurrentManagedThreadId}).");
        }

        var firstName = viewModel.Students[0].DisplayName;
        window.FilterStudentsForSmoke(firstName);
        if (viewModel.Students.Count == 0 || viewModel.Students.Count >= initialCount)
        {
            throw new InvalidOperationException(
                $"Student filter failed (filtered count={viewModel.Students.Count}).");
        }

        window.FilterStudentsForSmoke(string.Empty);
        if (viewModel.Students.Count != initialCount)
        {
            throw new InvalidOperationException("Student filter reset failed.");
        }

        setPassed(true);
    }
    catch (Exception ex)
    {
        setFailure($"{ex.GetType().Name}: {ex.Message}");
    }
    finally
    {
        app.Quit();
    }
}

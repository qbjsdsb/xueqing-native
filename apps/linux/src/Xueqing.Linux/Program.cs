using System.Runtime.Versioning;
using Xueqing.Linux;
using Xueqing.Linux.Infrastructure.Security;

if (!OperatingSystem.IsLinux())
{
    Console.Error.WriteLine("Xueqing.Linux only runs on Linux.");
    return 1;
}

return await RunLinuxAsync(args);

[SupportedOSPlatform("linux")]
static async Task<int> RunLinuxAsync(string[] args)
{
    if (args.Contains("--help", StringComparer.Ordinal) || args.Contains("-h", StringComparer.Ordinal))
    {
        Console.WriteLine("Xueqing.Linux — Native Linux Desktop Client (GTK4 + libadwaita + .NET 10)");
        Console.WriteLine("Usage:");
        Console.WriteLine("  Xueqing.Linux [xueqing://...]         Launch production client (requires deployment_profile.json)");
        Console.WriteLine("  Xueqing.Linux --prototype [URI]       Launch prototype dataset + real encrypted local draft store");
        Console.WriteLine("  Xueqing.Linux --security-check        Verify libsecret + SQLite3MC 2.4.0 + boot_id chain");
        Console.WriteLine("  Xueqing.Linux --smoke-gui             Run automated GTK4/Adwaita + xueqing:// smoke check");
        return 0;
    }

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

    using var app = new XueqingLinuxApplication(args);
    return app.Run();
}

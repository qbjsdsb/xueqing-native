using System.Globalization;
using System.Text.Json;
using Windows.ApplicationModel;
using Windows.Storage;
using Xueqing.Windows.Core.Sync;
using Xueqing.Windows.Infrastructure.Sync;
using Xueqing.Windows.LocalData;

namespace Xueqing.Windows.Integration;

internal static class WindowsPackagedAppIntegrationProbe
{
    private const string EnabledEnvironmentVariable = "XUEQING_WINDOWS_INTEGRATION_PROBE";
    private const string ReportFileName = "xueqing-app-integration.json";
    private const string PayloadMarker = "FICTIONAL-WINDOWS-REAL-APP-DURABLE-INTENT-MARKER";
    private static readonly Guid StableOperationId = new("a67af44f-c129-4a79-82b9-b79efd7d4e50");
    private static readonly DateTimeOffset StableCreatedAtUtc = new(2026, 9, 16, 0, 0, 0, TimeSpan.Zero);

    public static bool IsEnabled()
        => string.Equals(
            Environment.GetEnvironmentVariable(EnabledEnvironmentVariable),
            "1",
            StringComparison.Ordinal);

    public static async Task RunAndWriteReportAsync(CancellationToken cancellationToken = default)
    {
        var applicationData = ApplicationData.Current;
        var reportPath = Path.Combine(applicationData.LocalFolder.Path, ReportFileName);

        try
        {
            var report = await RunAsync(applicationData, cancellationToken);
            await WriteReportAsync(reportPath, report, cancellationToken);
        }
        catch (Exception exception)
        {
            var failure = new WindowsPackagedAppIntegrationReport(
                Succeeded: false,
                PackageName: TryGetPackageName(),
                PackageFamilyName: TryGetPackageFamilyName(),
                PackageVersion: TryGetPackageVersion(),
                StableOperationId: StableOperationId.ToString("D"),
                OutboxCount: null,
                QueueStatus: null,
                EnqueueDisposition: null,
                InstallationId: null,
                DatabaseRelativePath: null,
                FailureType: exception.GetType().FullName ?? exception.GetType().Name);

            await WriteReportAsync(reportPath, failure, cancellationToken);
        }
    }

    private static async Task<WindowsPackagedAppIntegrationReport> RunAsync(
        ApplicationData applicationData,
        CancellationToken cancellationToken)
    {
        var scope = WindowsLocalDataScope.CreateFictionalIntegrationScope(applicationData);
        var databasePath = WindowsLocalStatePaths.GetDurableIntentDatabasePath(
            applicationData.LocalFolder,
            scope);

        var store = new SqliteDurableOutboxStore(databasePath);
        var intent = new OutboxCommandIntent(
            StableOperationId,
            "integration_probe_append_evidence",
            "case-fictional-integration-001",
            "org-fictional-001/student-fictional-001/subject-chinese",
            1,
            "assignment-fictional-integration-001",
            JsonSerializer.Serialize(new { marker = PayloadMarker }),
            StableCreatedAtUtc);

        var enqueue = await store.EnqueueAsync(intent, cancellationToken);
        var restored = await store.GetAsync(StableOperationId, cancellationToken)
            ?? throw new InvalidOperationException("Integration Outbox operation was not readable after enqueue.");
        var count = await store.CountAsync(cancellationToken);

        if (count != 1)
        {
            throw new InvalidOperationException("Integration Outbox must contain exactly one stable operation.");
        }

        if (restored.Intent != intent)
        {
            throw new InvalidOperationException("Integration Outbox durable intent changed after persistence.");
        }

        if (restored.QueueStatus != OutboxQueueStatus.Pending || restored.AttemptCount != 0)
        {
            throw new InvalidOperationException("Integration Outbox operation did not remain pending and unattempted.");
        }

        var package = Package.Current;
        return new WindowsPackagedAppIntegrationReport(
            Succeeded: true,
            PackageName: package.Id.Name,
            PackageFamilyName: package.Id.FamilyName,
            PackageVersion: FormatVersion(package.Id.Version),
            StableOperationId: StableOperationId.ToString("D"),
            OutboxCount: count,
            QueueStatus: restored.QueueStatus.ToString(),
            EnqueueDisposition: enqueue.Disposition.ToString(),
            InstallationId: scope.InstallationId.ToString("D"),
            DatabaseRelativePath: Path.GetRelativePath(applicationData.LocalFolder.Path, databasePath)
                .Replace('\\', '/'),
            FailureType: null);
    }

    private static async Task WriteReportAsync(
        string reportPath,
        WindowsPackagedAppIntegrationReport report,
        CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(
            report,
            new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true,
            });

        var temporaryPath = reportPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temporaryPath, json, cancellationToken);
            File.Move(temporaryPath, reportPath, overwrite: true);
        }
        finally
        {
            try
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private static string TryGetPackageName()
    {
        try
        {
            return Package.Current.Id.Name;
        }
        catch
        {
            return "<unavailable>";
        }
    }

    private static string TryGetPackageFamilyName()
    {
        try
        {
            return Package.Current.Id.FamilyName;
        }
        catch
        {
            return "<unavailable>";
        }
    }

    private static string TryGetPackageVersion()
    {
        try
        {
            return FormatVersion(Package.Current.Id.Version);
        }
        catch
        {
            return "<unavailable>";
        }
    }

    private static string FormatVersion(PackageVersion version)
        => string.Create(
            CultureInfo.InvariantCulture,
            $"{version.Major}.{version.Minor}.{version.Build}.{version.Revision}");

    private sealed record WindowsPackagedAppIntegrationReport(
        bool Succeeded,
        string PackageName,
        string PackageFamilyName,
        string PackageVersion,
        string StableOperationId,
        long? OutboxCount,
        string? QueueStatus,
        string? EnqueueDisposition,
        string? InstallationId,
        string? DatabaseRelativePath,
        string? FailureType);
}

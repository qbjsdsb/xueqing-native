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
    private const string DiagnosticOutputEnvironmentVariable = "XUEQING_WINDOWS_INTEGRATION_DIAGNOSTIC_OUTPUT";
    private const string ReportFileName = "xueqing-app-integration.json";
    private const string PayloadMarker = "FICTIONAL-WINDOWS-REAL-APP-DURABLE-INTENT-MARKER";
    private const string FailureStageDataKey = "Xueqing.Windows.Integration.FailureStage";
    private static readonly Guid StableOperationId = new("a67af44f-c129-4a79-82b9-b79efd7d4e50");
    private static readonly DateTimeOffset StableCreatedAtUtc = new(2026, 9, 16, 0, 0, 0, TimeSpan.Zero);

    public static bool IsEnabled()
        => string.Equals(
            Environment.GetEnvironmentVariable(EnabledEnvironmentVariable),
            "1",
            StringComparison.Ordinal);

    public static async Task RunAndWriteReportAsync(CancellationToken cancellationToken = default)
    {
        string? reportPath = null;
        var failureStage = "application-data.current";

        try
        {
            var applicationData = ApplicationData.Current;

            failureStage = "report-path.resolve";
            reportPath = Path.Combine(applicationData.LocalFolder.Path, ReportFileName);

            failureStage = "probe.run";
            var report = await RunAsync(applicationData, cancellationToken);

            failureStage = "report.write";
            await WriteReportAsync(reportPath, report, cancellationToken);
        }
        catch (Exception exception)
        {
            if (exception.Data[FailureStageDataKey] is null)
            {
                exception.Data[FailureStageDataKey] = failureStage;
            }

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
                FailureType: FormatFailure(exception));

            await TryWriteReportAsync(reportPath, failure, cancellationToken);
            await TryWriteDiagnosticMirrorAsync(failure, cancellationToken);
        }
    }

    private static async Task<WindowsPackagedAppIntegrationReport> RunAsync(
        ApplicationData applicationData,
        CancellationToken cancellationToken)
    {
        var failureStage = "scope.create";

        try
        {
            var scope = WindowsLocalDataScope.CreateFictionalIntegrationScope(applicationData);

            failureStage = "database-path.resolve";
            var databasePath = WindowsLocalStatePaths.GetDurableIntentDatabasePath(
                applicationData.LocalFolder,
                scope);

            failureStage = "store.create";
            var store = new SqliteDurableOutboxStore(databasePath);

            failureStage = "intent.compose";
            var intent = new OutboxCommandIntent(
                StableOperationId,
                "integration_probe_append_evidence",
                "case-fictional-integration-001",
                "org-fictional-001/student-fictional-001/subject-chinese",
                1,
                "assignment-fictional-integration-001",
                JsonSerializer.Serialize(new { marker = PayloadMarker }),
                StableCreatedAtUtc);

            failureStage = "outbox.enqueue";
            var enqueue = await store.EnqueueAsync(intent, cancellationToken);

            failureStage = "outbox.read";
            var restored = await store.GetAsync(StableOperationId, cancellationToken)
                ?? throw new InvalidOperationException("Integration Outbox operation was not readable after enqueue.");

            failureStage = "outbox.count";
            var count = await store.CountAsync(cancellationToken);

            failureStage = "outbox.count.validate";
            if (count != 1)
            {
                throw new InvalidOperationException("Integration Outbox must contain exactly one stable operation.");
            }

            failureStage = "outbox.intent.validate";
            if (restored.Intent != intent)
            {
                throw new InvalidOperationException("Integration Outbox durable intent changed after persistence.");
            }

            failureStage = "outbox.queue-state.validate";
            if (restored.QueueStatus != OutboxQueueStatus.Pending || restored.AttemptCount != 0)
            {
                throw new InvalidOperationException("Integration Outbox operation did not remain pending and unattempted.");
            }

            failureStage = "package.read";
            var package = Package.Current;

            failureStage = "report.compose";
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
        catch (Exception exception)
        {
            exception.Data[FailureStageDataKey] = failureStage;
            throw;
        }
    }

    private static async Task TryWriteReportAsync(
        string? reportPath,
        WindowsPackagedAppIntegrationReport report,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(reportPath))
        {
            return;
        }

        try
        {
            await WriteReportAsync(reportPath, report, cancellationToken);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static async Task TryWriteDiagnosticMirrorAsync(
        WindowsPackagedAppIntegrationReport report,
        CancellationToken cancellationToken)
    {
        var diagnosticPath = Environment.GetEnvironmentVariable(DiagnosticOutputEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(diagnosticPath))
        {
            return;
        }

        try
        {
            var fullPath = Path.GetFullPath(diagnosticPath);
            var directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await WriteReportAsync(fullPath, report, cancellationToken);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
        catch (ArgumentException)
        {
        }
        catch (NotSupportedException)
        {
        }
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

    private static string FormatFailure(Exception exception)
    {
        var stage = exception.Data[FailureStageDataKey] as string ?? "probe.run";
        var type = exception.GetType().FullName ?? exception.GetType().Name;
        var result = $"stage={stage}; type={type}; hresult=0x{exception.HResult:X8}";

        if (exception.InnerException is { } innerException)
        {
            var innerType = innerException.GetType().FullName ?? innerException.GetType().Name;
            result += $"; innerType={innerType}; innerHResult=0x{innerException.HResult:X8}";
        }

        return result;
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

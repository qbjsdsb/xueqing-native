using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Xueqing.Windows.Core.Services;

namespace Xueqing.Windows.Infrastructure.Support;

public static partial class DiagnosticsArchiveWriter
{
    private const string FileName = "diagnostics.json";

    public static void Write(DiagnosticsSnapshot snapshot, Stream output)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(output);
        Validate(snapshot);
        var json = Serialize(snapshot);

        using var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);
        var entry = archive.CreateEntry(FileName, CompressionLevel.Optimal);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(json);
    }

    public static string Serialize(DiagnosticsSnapshot snapshot)
    {
        Validate(snapshot);
        var payload = new
        {
            contract = "diagnostics_manifest_v1",
            generated_at = snapshot.GeneratedAt,
            platform = new
            {
                kind = "windows",
                os_version = snapshot.OsVersion,
                architecture = snapshot.Architecture,
                package_identity = snapshot.PackageIdentity,
            },
            app = new
            {
                version = snapshot.AppVersion,
                source_commit = snapshot.SourceCommit,
                client_contract_version = snapshot.ClientContractVersion,
                local_schema_version = snapshot.LocalSchemaVersion,
            },
            deployment = new
            {
                profile_id = snapshot.Deployment.ProfileId,
                environment_id = snapshot.Deployment.EnvironmentId,
                trust_domain_id = snapshot.Deployment.TrustDomainId,
                provider_id = snapshot.Deployment.ProviderId,
            },
            runtime = new
            {
                session_state = SessionWire(snapshot.SessionState),
                last_sync_category = SyncWire(snapshot.LastSyncCategory),
            },
            compatibility = new
            {
                state = CompatibilityWire(snapshot.Compatibility.State),
                reason_code = snapshot.Compatibility.ReasonCode,
                policy_revision = snapshot.Compatibility.PolicyRevision,
            },
            queues = new
            {
                pending_intents = snapshot.Queues.PendingIntents,
                outbox_items = snapshot.Queues.OutboxItems,
                attachment_staging = snapshot.Queues.AttachmentStaging,
            },
            recent_error_codes = snapshot.RecentErrorCodes,
            archive_files = new[]
            {
                new { name = FileName, contract = "diagnostics_manifest_v1" },
            },
        };

        return JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
    }

    private static void Validate(DiagnosticsSnapshot value)
    {
        Require(value.OsVersion, 128, nameof(value.OsVersion));
        Require(value.Architecture, 32, nameof(value.Architecture));
        Require(value.PackageIdentity, 160, nameof(value.PackageIdentity));
        Require(value.AppVersion, 64, nameof(value.AppVersion));
        if (!CommitRegex().IsMatch(value.SourceCommit))
        {
            throw new ArgumentException("Source commit is invalid.", nameof(value));
        }
        if (value.ClientContractVersion < 1 || value.LocalSchemaVersion < 1)
        {
            throw new ArgumentException("Contract/schema versions must be positive.", nameof(value));
        }

        Require(value.Deployment.ProfileId, 64, nameof(value.Deployment.ProfileId));
        Require(value.Deployment.EnvironmentId, 64, nameof(value.Deployment.EnvironmentId));
        Require(value.Deployment.TrustDomainId, 96, nameof(value.Deployment.TrustDomainId));
        Require(value.Deployment.ProviderId, 32, nameof(value.Deployment.ProviderId));

        if (value.Queues.PendingIntents is < 0 ||
            value.Queues.OutboxItems is < 0 ||
            value.Queues.AttachmentStaging is < 0)
        {
            throw new ArgumentException("Diagnostic queue counts cannot be negative.", nameof(value));
        }

        if (value.RecentErrorCodes.Count > 20 ||
            value.RecentErrorCodes.Any(code => !ErrorCodeRegex().IsMatch(code)))
        {
            throw new ArgumentException("Diagnostic error codes are invalid.", nameof(value));
        }

        if (value.Compatibility.ReasonCode is not null &&
            !ErrorCodeRegex().IsMatch(value.Compatibility.ReasonCode))
        {
            throw new ArgumentException("Compatibility reason is invalid.", nameof(value));
        }
        if (value.Compatibility.PolicyRevision is not null &&
            !PolicyRevisionRegex().IsMatch(value.Compatibility.PolicyRevision))
        {
            throw new ArgumentException("Compatibility policy revision is invalid.", nameof(value));
        }
    }

    private static void Require(string value, int maxLength, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, name);
        if (value.Length > maxLength || value != value.Trim())
        {
            throw new ArgumentException("Diagnostic field is invalid.", name);
        }
    }

    private static string SessionWire(DiagnosticSessionState value) => value switch
    {
        DiagnosticSessionState.SignedOut => "signed_out",
        DiagnosticSessionState.Authenticated => "authenticated",
        DiagnosticSessionState.RefreshRequired => "refresh_required",
        DiagnosticSessionState.RevokedOrInvalid => "revoked_or_invalid",
        DiagnosticSessionState.ConfigurationUnavailable => "configuration_unavailable",
        _ => throw new ArgumentOutOfRangeException(nameof(value)),
    };

    private static string SyncWire(DiagnosticSyncCategory value) => value switch
    {
        DiagnosticSyncCategory.Never => "never",
        DiagnosticSyncCategory.Recent => "recent",
        DiagnosticSyncCategory.Stale => "stale",
        DiagnosticSyncCategory.Unknown => "unknown",
        _ => throw new ArgumentOutOfRangeException(nameof(value)),
    };

    private static string CompatibilityWire(DiagnosticCompatibilityState value) => value switch
    {
        DiagnosticCompatibilityState.Supported => "supported",
        DiagnosticCompatibilityState.UpdateRecommended => "update_recommended",
        DiagnosticCompatibilityState.UpdateRequired => "update_required",
        DiagnosticCompatibilityState.SecurityBlocked => "security_blocked",
        DiagnosticCompatibilityState.Unknown => "unknown",
        _ => throw new ArgumentOutOfRangeException(nameof(value)),
    };

    [GeneratedRegex("^[0-9a-f]{40}$", RegexOptions.CultureInvariant)]
    private static partial Regex CommitRegex();

    [GeneratedRegex("^XQ_[A-Z0-9_]{3,64}$", RegexOptions.CultureInvariant)]
    private static partial Regex ErrorCodeRegex();

    [GeneratedRegex("^[a-z0-9][a-z0-9._-]{1,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex PolicyRevisionRegex();
}

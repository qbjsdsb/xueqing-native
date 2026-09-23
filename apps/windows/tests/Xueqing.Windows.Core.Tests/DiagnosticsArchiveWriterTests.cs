using System.IO.Compression;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Xueqing.Windows.Core.Services;
using Xueqing.Windows.Infrastructure.Support;

namespace Xueqing.Windows.Core.Tests;

[TestClass]
public sealed class DiagnosticsArchiveWriterTests
{
    [TestMethod]
    public void Archive_contains_only_allowlisted_diagnostics_json()
    {
        using var output = new MemoryStream();
        DiagnosticsArchiveWriter.Write(Snapshot(), output);
        output.Position = 0;

        using var zip = new ZipArchive(output, ZipArchiveMode.Read);
        Assert.AreEqual(1, zip.Entries.Count);
        Assert.AreEqual("diagnostics.json", zip.Entries[0].FullName);
        using var reader = new StreamReader(zip.Entries[0].Open());
        var body = reader.ReadToEnd();
        StringAssert.Contains(body, "\"diagnostics_manifest_v1\"");
        StringAssert.Contains(body, "\"authenticated\"");
    }

    [TestMethod]
    public void Freeform_private_teaching_text_cannot_enter_error_codes()
    {
        var bad = Snapshot() with
        {
            RecentErrorCodes = ["虚构学生甲：这段课堂观察绝不能进入诊断包"],
        };

        Assert.Throws<ArgumentException>(() => DiagnosticsArchiveWriter.Serialize(bad));
    }

    [TestMethod]
    public void Serialized_manifest_has_no_credential_or_teaching_fields()
    {
        var body = DiagnosticsArchiveWriter.Serialize(Snapshot()).ToLowerInvariant();

        foreach (var forbidden in new[]
        {
            "student_name",
            "observation_text",
            "access_token",
            "refresh_token",
            "authorization",
            "attachment_bytes",
            "email",
            "password",
        })
        {
            Assert.IsFalse(body.Contains(forbidden, StringComparison.Ordinal));
        }
    }

    private static DiagnosticsSnapshot Snapshot() => new(
        DateTimeOffset.Parse("2026-09-23T04:00:00Z"),
        "10.0.26100",
        "x64",
        "Xueqing.Native.Development",
        "1.0.0.0",
        "62cad1dfce913775776e4cbd60add6ecab1aefc2",
        1,
        3,
        new DiagnosticDeployment(
            "prod-singapore-v1",
            "production",
            "xueqing-prod-sg",
            "supabase"),
        DiagnosticSessionState.Authenticated,
        DiagnosticSyncCategory.Recent,
        new DiagnosticCompatibility(
            DiagnosticCompatibilityState.Supported,
            "XQ_CLIENT_SUPPORTED",
            "v1-initial"),
        new DiagnosticQueueCounts(1, 1, 0),
        ["XQ_NETWORK_TIMEOUT"]);
}

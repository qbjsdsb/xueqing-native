using System.Collections.Concurrent;
using System.IO.Compression;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Xueqing.Linux.Infrastructure.Agent;
using Xueqing.Linux.Infrastructure.Integration;
using Xueqing.Linux.Infrastructure.LocalData;
using Xueqing.Linux.Infrastructure.Security;
using Xueqing.Windows.Core.Agent;
using Xueqing.Windows.Core.Models;
using Xueqing.Windows.Core.Services;
using Xueqing.Windows.Infrastructure.Auth;
using Xueqing.Windows.Infrastructure.LocalData;

[assembly: SupportedOSPlatform("linux")]

namespace Xueqing.Linux.Tests;

[TestClass]
public sealed class LinuxInfrastructureTests
{
    [TestMethod]
    public void OfflineAccessClock_ReturnsConsistentBootIdAndNonDecreasingMonotonicMs()
    {
        var first = LinuxOfflineAccessClock.Sample();
        var second = LinuxOfflineAccessClock.Sample();

        Assert.IsTrue(Guid.TryParse(first.BootSessionId, out var bootId) && bootId != Guid.Empty);
        Assert.AreEqual(first.BootSessionId, second.BootSessionId);
        Assert.IsTrue(second.MonotonicElapsedMilliseconds >= first.MonotonicElapsedMilliseconds);
    }

    [TestMethod]
    public void OfflineAccessClock_FailsClosedWhenBootIdMissingOrMalformed()
    {
        var tempDir = CreateTempDir();
        try
        {
            var missing = Path.Combine(tempDir, "missing-boot-id");
            Assert.ThrowsExactly<InvalidOperationException>(() => LinuxOfflineAccessClock.Sample(missing));

            var malformed = Path.Combine(tempDir, "bad-boot-id");
            File.WriteAllText(malformed, "not-a-uuid\n");
            Assert.ThrowsExactly<InvalidOperationException>(() => LinuxOfflineAccessClock.Sample(malformed));
        }
        finally
        {
            CleanupDir(tempDir);
        }
    }

    [TestMethod]
    public void LocalStatePaths_CreatesStableInstallationIdAndIsolatesScopes()
    {
        var tempDir = CreateTempDir();
        try
        {
            var id1 = LinuxLocalStatePaths.LoadOrCreateInstallationId(tempDir);
            var id2 = LinuxLocalStatePaths.LoadOrCreateInstallationId(tempDir);
            Assert.AreNotEqual(Guid.Empty, id1);
            Assert.AreEqual(id1, id2);

            var pathA = LinuxLocalStatePaths.GetObservationDraftDatabasePath("staging", "user-1", "org-1", id1, tempDir);
            var pathB = LinuxLocalStatePaths.GetObservationDraftDatabasePath("staging", "user-1", "org-2", id1, tempDir);
            var pathIndex = LinuxLocalStatePaths.GetOnlineCommandRecoveryIndexDatabasePath("staging", "user-1", id1, tempDir);

            Assert.AreNotEqual(pathA, pathB);
            Assert.IsTrue(pathA.EndsWith("observation-draft.db", StringComparison.Ordinal));
            Assert.IsTrue(pathIndex.EndsWith("online-command-recovery-index.db", StringComparison.Ordinal));
            Assert.IsTrue(pathIndex.Contains(Path.Combine("scoped-local-data", "v1", "actor"), StringComparison.Ordinal));
        }
        finally
        {
            CleanupDir(tempDir);
        }
    }

    [TestMethod]
    public void ProtocolActivation_ParsesCanonicalUrisAndFailsClosedOnInvalidInputs()
    {
        var entityId = Guid.Parse("11111111-2222-4333-8444-555555555555");

        var today = LinuxProtocolActivation.ParseUri("xueqing://today");
        Assert.IsNotNull(today);
        Assert.AreEqual(AgentNavigationTargetKind.Today, today.Kind);
        Assert.IsNull(today.EntityId);

        var student = LinuxProtocolActivation.ParseUri($"xueqing://student/{entityId:D}");
        Assert.IsNotNull(student);
        Assert.AreEqual(AgentNavigationTargetKind.Student, student.Kind);
        Assert.AreEqual(entityId, student.EntityId);

        var learning = LinuxProtocolActivation.ParseUri($"xueqing://learning/{entityId:D}");
        Assert.IsNotNull(learning);
        Assert.AreEqual(AgentNavigationTargetKind.LearningCase, learning.Kind);
        Assert.AreEqual(entityId, learning.EntityId);

        // Fail closed on queries, fragments, userinfo, extra segments, unknown hosts, empty GUID
        Assert.IsNull(LinuxProtocolActivation.ParseUri($"xueqing://student/{entityId:D}?org=evil"));
        Assert.IsNull(LinuxProtocolActivation.ParseUri($"xueqing://student/{entityId:D}#frag"));
        Assert.IsNull(LinuxProtocolActivation.ParseUri($"xueqing://user@student/{entityId:D}"));
        Assert.IsNull(LinuxProtocolActivation.ParseUri($"xueqing://student/{entityId:D}/extra"));
        Assert.IsNull(LinuxProtocolActivation.ParseUri($"xueqing://admin/{entityId:D}"));
        Assert.IsNull(LinuxProtocolActivation.ParseUri("xueqing://student/00000000-0000-0000-0000-000000000000"));
        Assert.IsNull(LinuxProtocolActivation.ParseUri("https://example.com"));

        // Argument list extraction ignores flags and extracts canonical URI
        var fromArgs = LinuxProtocolActivation.ParseArguments(["--prototype", $"xueqing://student/{entityId:D}"]);
        Assert.IsNotNull(fromArgs);
        Assert.AreEqual(entityId, fromArgs.EntityId);
    }

    [TestMethod]
    public void ProtocolActivation_ResolvesPrototypeStudentUuidDeterministically()
    {
        var students = SyntheticDataFactory.CreateStudents(20);
        var target = students[7];
        var uuid = PrototypeDraftIdentity.ResolveStudentUuid(target.Id);

        var found = LinuxProtocolActivation.FindPrototypeStudentByUuid(uuid, students);
        Assert.IsNotNull(found);
        Assert.AreEqual(target.Id, found.Id);

        Assert.IsNull(LinuxProtocolActivation.FindPrototypeStudentByUuid(Guid.NewGuid(), students));
    }

    [TestMethod]
    public async Task ScopedLocalStores_EncryptAllDatabasesAndPreserveRecoveryInvariants()
    {
        var tempDir = CreateTempDir();
        try
        {
            var keyStoreFactory = CreateInMemoryKeyStoreFactory();
            var context = new LinuxLocalStoreContext("test-env", tempDir, keyStoreFactory);

            var actorId = Guid.NewGuid();
            var orgId = Guid.NewGuid();
            var studentId = Guid.NewGuid();
            var subjectId = Guid.NewGuid();
            var assignmentId = Guid.NewGuid();
            var draftScope = new ObservationDraftScope(orgId, studentId, subjectId, assignmentId);

            // 1. Encrypted Observation Draft with optimistic epoch protection
            var draftStore = new LinuxObservationDraftStore(context);
            var opened = await draftStore.OpenAsync(actorId, draftScope);
            const string draftMarker = "FICTIONAL-LINUX-DRAFT-SECRET-MARKER-2026";
            var saved = await draftStore.SaveAsync(actorId, draftScope, opened.Epoch, draftMarker);
            Assert.IsTrue(saved);
            var staleSave = await draftStore.SaveAsync(actorId, draftScope, opened.Epoch + 99, "stale");
            Assert.IsFalse(staleSave);

            var reopened = await draftStore.OpenAsync(actorId, draftScope);
            Assert.IsNotNull(reopened.Recovered);
            Assert.AreEqual(draftMarker, reopened.Recovered.Text);

            // Verify zero plaintext leakage on disk
            var draftDbPath = context.GetOrgDbPath(actorId, orgId, "observation-draft.db");
            AssertNoPlaintext(draftDbPath, draftMarker);

            // 2. CreateLearningCaseRecoveryStore discovers pending items via actor index even if membership is removed
            var caseRecoveryStore = new LinuxCreateLearningCaseRecoveryStore(context);
            var opId = Guid.NewGuid();
            var obsId = Guid.NewGuid();
            var req = new CreateLearningCaseRequest(
                opId,
                orgId,
                studentId,
                subjectId,
                assignmentId,
                "FICTIONAL-CASE-TITLE-MUST-BE-ENCRYPTED",
                "FICTIONAL-PRIMARY-ACTION",
                new DateOnly(2026, 12, 1),
                obsId);
            await caseRecoveryStore.SaveAsync(actorId, req);

            var pendingEvenWhenRemoved = await caseRecoveryStore.ListPendingAsync(actorId, Array.Empty<Guid>());
            Assert.AreEqual(1, pendingEvenWhenRemoved.Count);
            Assert.AreEqual(opId, pendingEvenWhenRemoved[0].OperationId);
            AssertNoPlaintext(
                context.GetOrgDbPath(actorId, orgId, "online-command-recovery.db"),
                "FICTIONAL-CASE-TITLE-MUST-BE-ENCRYPTED");

            await caseRecoveryStore.RemoveAsync(actorId, orgId, opId);
            var afterRemove = await caseRecoveryStore.ListPendingAsync(actorId, Array.Empty<Guid>());
            Assert.AreEqual(0, afterRemove.Count);

            // 3. ActionProgressionRecoveryStore save + find + remove
            var actionStore = new LinuxActionProgressionRecoveryStore(context);
            var actionOpId = Guid.NewGuid();
            var caseId = Guid.NewGuid();
            var primaryActionId = Guid.NewGuid();
            var rescheduleIntent = new ReschedulePrimaryActionRecoveryIntent(
                new ReschedulePrimaryActionRequest(
                    actionOpId,
                    orgId,
                    studentId,
                    subjectId,
                    assignmentId,
                    caseId,
                    primaryActionId,
                    1,
                    1,
                    new DateOnly(2026, 12, 10)));
            await actionStore.SaveAsync(actorId, rescheduleIntent);
            var foundAction = await actionStore.FindByPrimaryActionAsync(actorId, orgId, caseId, primaryActionId);
            Assert.IsNotNull(foundAction);
            Assert.AreEqual(actionOpId, foundAction.OperationId);
            await actionStore.RemoveAsync(actorId, orgId, actionOpId);
            Assert.IsNull(await actionStore.FindByPrimaryActionAsync(actorId, orgId, caseId, primaryActionId));

            // 4. CaseLifecycleRecoveryStore save + find + remove
            var lifecycleStore = new LinuxCaseLifecycleRecoveryStore(context);
            var lifecycleOpId = Guid.NewGuid();
            var closeIntent = new CloseCaseRecoveryIntent(
                new CloseLearningCaseRequest(
                    lifecycleOpId,
                    orgId,
                    studentId,
                    subjectId,
                    assignmentId,
                    caseId,
                    primaryActionId,
                    2,
                    1));
            await lifecycleStore.SaveAsync(actorId, closeIntent);
            var foundLifecycle = await lifecycleStore.FindByCaseAsync(actorId, orgId, caseId);
            Assert.IsNotNull(foundLifecycle);
            Assert.AreEqual(lifecycleOpId, foundLifecycle.OperationId);
            await lifecycleStore.RemoveAsync(actorId, orgId, lifecycleOpId);
            Assert.IsNull(await lifecycleStore.FindByCaseAsync(actorId, orgId, caseId));

            // 5. OrganizationInvitationRecoveryStore save + list + remove
            var inviteStore = new LinuxOrganizationInvitationRecoveryStore(context);
            var inviteOpId = Guid.NewGuid();
            var inviteIntent = new OrganizationInvitationRecoveryIntent(
                inviteOpId,
                Guid.NewGuid(),
                orgId,
                "teacher.fictional@example.test",
                OrganizationInvitationTargetRole.Teacher,
                true,
                null,
                OrganizationInvitationRecoveryStage.CreatePending,
                null);
            await inviteStore.SaveAsync(actorId, inviteIntent);
            var invites = await inviteStore.ListAsync(actorId, orgId);
            Assert.AreEqual(1, invites.Count);
            Assert.AreEqual(inviteOpId, invites[0].CreateOperationId);
            await inviteStore.RemoveAsync(actorId, orgId, inviteOpId);
            Assert.AreEqual(0, (await inviteStore.ListAsync(actorId, orgId)).Count);
        }
        finally
        {
            CleanupDir(tempDir);
        }
    }

    [TestMethod]
    public void ProductionClientRuntime_ProducesValidLinuxDiagnosticsArchive()
    {
        var tempDir = CreateTempDir();
        try
        {
            var profilePath = Path.Combine(tempDir, "deployment_profile.json");
            File.WriteAllText(profilePath, """
                {
                  "contract": "deployment_profile_v1",
                  "profile_id": "staging-linux-test",
                  "environment_id": "staging",
                  "trust_domain_id": "xueqing-staging-trust",
                  "provider_id": "supabase",
                  "project_origin": "https://fictional.supabase.co",
                  "publishable_key": "sb_publishable_fictional_test_key_0000000000000000",
                  "required_edge_region": "ap-northeast-1",
                  "capabilities": [
                    "auth",
                    "database-rpc",
                    "edge-functions",
                    "private-storage"
                  ],
                  "source": {
                    "repository": "qbjsdsb/xueqing-native",
                    "commit": "0123456789abcdef0123456789abcdef01234567"
                  }
                }
                """);

            using var runtime = LinuxProductionClientRuntime.Create(
                profilePath,
                tempDir,
                new StubRefreshTokenVault(),
                CreateInMemoryKeyStoreFactory());

            var archiveBytes = runtime.CreateDiagnosticsArchive();
            Assert.IsTrue(archiveBytes.Length > 0);

            using var zip = new ZipArchive(new MemoryStream(archiveBytes), ZipArchiveMode.Read);
            var manifestEntry = zip.GetEntry("diagnostics.json");
            Assert.IsNotNull(manifestEntry);

            using var reader = new StreamReader(manifestEntry.Open(), Encoding.UTF8);
            var manifestJson = reader.ReadToEnd();
            using var doc = JsonDocument.Parse(manifestJson);
            var root = doc.RootElement;

            Assert.AreEqual("linux", root.GetProperty("platform").GetProperty("kind").GetString());
            Assert.AreEqual("com.xueqing.app", root.GetProperty("platform").GetProperty("package_identity").GetString());
            Assert.AreEqual("1.0.0-rc.1", root.GetProperty("app").GetProperty("version").GetString());
            Assert.AreEqual("signed_out", root.GetProperty("runtime").GetProperty("session_state").GetString());
        }
        finally
        {
            CleanupDir(tempDir);
        }
    }

    private static Func<string, ILocalDatabaseKeyStore> CreateInMemoryKeyStoreFactory()
    {
        var keys = new ConcurrentDictionary<string, byte[]>(StringComparer.Ordinal);
        return path => new InMemoryDatabaseKeyStore(Path.GetFullPath(path), keys);
    }

    private static void AssertNoPlaintext(string filePath, string marker)
    {
        Assert.IsTrue(File.Exists(filePath), $"Expected encrypted DB file at {filePath}");
        var bytes = File.ReadAllBytes(filePath);
        var needle = Encoding.UTF8.GetBytes(marker);
        Assert.IsTrue(bytes.AsSpan().IndexOf(needle) < 0, $"Plaintext marker leaked into {filePath}");
    }

    private static string CreateTempDir()
    {
        var path = Path.Combine(Path.GetTempPath(), $"xueqing-linux-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static void CleanupDir(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
            // Best-effort temp cleanup.
        }
    }

    private sealed class InMemoryDatabaseKeyStore : ILocalDatabaseKeyStore
    {
        private readonly string _path;
        private readonly ConcurrentDictionary<string, byte[]> _keys;

        public InMemoryDatabaseKeyStore(string path, ConcurrentDictionary<string, byte[]> keys)
        {
            _path = path;
            _keys = keys;
        }

        public Task<byte[]> LoadOrCreateAsync(CancellationToken cancellationToken = default)
        {
            var key = _keys.GetOrAdd(_path, static _ => RandomNumberGenerator.GetBytes(32));
            return Task.FromResult(key.ToArray());
        }
    }

    private sealed class StubRefreshTokenVault : IRefreshTokenVault
    {
        private string? _token;
        public ValueTask<string?> LoadAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(_token);
        public ValueTask StoreAsync(string refreshToken, CancellationToken cancellationToken = default)
        {
            _token = refreshToken;
            return ValueTask.CompletedTask;
        }

        public ValueTask ClearAsync(CancellationToken cancellationToken = default)
        {
            _token = null;
            return ValueTask.CompletedTask;
        }
    }
}

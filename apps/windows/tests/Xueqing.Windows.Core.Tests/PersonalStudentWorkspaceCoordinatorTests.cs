using Microsoft.VisualStudio.TestTools.UnitTesting;
using Xueqing.Windows.Core.Models;
using Xueqing.Windows.Core.Services;

namespace Xueqing.Windows.Core.Tests;

[TestClass]
public sealed class PersonalStudentWorkspaceCoordinatorTests
{
    private static readonly Guid ActorId = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid OrganizationId = Guid.Parse("20000000-0000-0000-0000-000000000001");
    private static readonly Guid StudentId = Guid.Parse("30000000-0000-0000-0000-000000000001");

    [TestMethod]
    public async Task Bootstrap_groups_multiple_subjects_under_one_student_without_losing_scope()
    {
        var chinese = Context("40000000-0000-0000-0000-000000000001", "50000000-0000-0000-0000-000000000001", "chinese");
        var math = Context("40000000-0000-0000-0000-000000000002", "50000000-0000-0000-0000-000000000002", "math");
        var bootstrap = Snapshot(chinese, math);
        var workspace = CreateWorkspace(new ImmediateBootstrapReader(PersonalBootstrapReadResult.Success(bootstrap)), out _);

        var state = await workspace.RefreshAsync();

        Assert.AreEqual(PersonalStudentWorkspaceStatus.Ready, state.Status);
        Assert.AreEqual(1, workspace.Students.Count);
        Assert.AreEqual(2, workspace.Students[0].TeachingContexts.Count);
        CollectionAssert.AreEqual(
            new[] { "chinese", "math" },
            workspace.Students[0].TeachingContexts.Select(context => context.SubjectKey).ToArray());
    }

    [TestMethod]
    public async Task Recent_read_uses_actor_and_scope_from_current_bootstrap_only()
    {
        var context = Context("40000000-0000-0000-0000-000000000001", "50000000-0000-0000-0000-000000000001", "chinese");
        var bootstrap = Snapshot(context);
        var workspace = CreateWorkspace(new ImmediateBootstrapReader(PersonalBootstrapReadResult.Success(bootstrap)), out var observationsReader);
        await workspace.RefreshAsync();

        await workspace.LoadRecentAsync(context);

        Assert.AreEqual(1, observationsReader.CallCount);
        Assert.AreEqual(ActorId, observationsReader.LastActorId);
        Assert.AreEqual(context.ObservationScope, observationsReader.LastScope);
    }

    [TestMethod]
    public async Task Context_not_in_current_bootstrap_never_reaches_recent_projection()
    {
        var current = Context("40000000-0000-0000-0000-000000000001", "50000000-0000-0000-0000-000000000001", "chinese");
        var stale = Context("40000000-0000-0000-0000-000000000009", "50000000-0000-0000-0000-000000000009", "physics");
        var workspace = CreateWorkspace(new ImmediateBootstrapReader(PersonalBootstrapReadResult.Success(Snapshot(current))), out var observationsReader);
        await workspace.RefreshAsync();

        var result = await workspace.LoadRecentAsync(stale);

        Assert.AreEqual(0, observationsReader.CallCount);
        Assert.AreEqual(StudentRecentObservationsViewStatus.Idle, result.Status);
        Assert.IsNull(result.Snapshot);
    }

    [TestMethod]
    public async Task Refresh_clears_old_recent_facts_before_revalidating_authority()
    {
        var context = Context("40000000-0000-0000-0000-000000000001", "50000000-0000-0000-0000-000000000001", "chinese");
        var bootstrap = Snapshot(context);
        var nextBootstrap = new TaskCompletionSource<PersonalBootstrapReadResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var bootstrapReader = new SequencedBootstrapReader(
            Task.FromResult(PersonalBootstrapReadResult.Success(bootstrap)),
            nextBootstrap.Task);
        var workspace = CreateWorkspace(bootstrapReader, out _);

        await workspace.RefreshAsync();
        await workspace.LoadRecentAsync(context);
        Assert.AreEqual(StudentRecentObservationsViewStatus.Data, workspace.RecentObservations.Status);

        var refresh = workspace.RefreshAsync();
        Assert.AreEqual(PersonalStudentWorkspaceStatus.Loading, workspace.Current.Status);
        Assert.AreEqual(StudentRecentObservationsViewStatus.Idle, workspace.RecentObservations.Status);

        nextBootstrap.SetResult(PersonalBootstrapReadResult.Failed(
            PersonalBootstrapFailureKind.AccessDenied,
            "XQ_ACTOR_DISABLED"));
        await refresh;

        Assert.AreEqual(PersonalStudentWorkspaceStatus.AccessDenied, workspace.Current.Status);
        Assert.IsNull(workspace.Current.Bootstrap);
        Assert.AreEqual(0, workspace.Students.Count);
    }

    private static PersonalStudentWorkspaceCoordinator CreateWorkspace(
        IPersonalBootstrapReader bootstrapReader,
        out RecordingObservationsReader observationsReader)
    {
        observationsReader = new RecordingObservationsReader();
        return new PersonalStudentWorkspaceCoordinator(
            bootstrapReader,
            new StudentRecentObservationsCoordinator(observationsReader));
    }

    private static PersonalTeachingContext Context(string subjectProfileId, string assignmentId, string subjectKey) =>
        new(
            OrganizationId,
            StudentId,
            "虚构学生0001",
            Guid.Parse(subjectProfileId),
            subjectKey,
            Guid.Parse(assignmentId));

    private static PersonalBootstrapSnapshot Snapshot(params PersonalTeachingContext[] contexts) =>
        new(
            DateTimeOffset.Parse("2026-09-17T12:00:00Z"),
            ActorId,
            "虚构老师",
            new[] { new PersonalOrganization(OrganizationId, "虚构机构", true) },
            contexts);

    private sealed class ImmediateBootstrapReader(PersonalBootstrapReadResult result) : IPersonalBootstrapReader
    {
        public Task<PersonalBootstrapReadResult> ReadAsync(CancellationToken cancellationToken = default) => Task.FromResult(result);
    }

    private sealed class SequencedBootstrapReader(params Task<PersonalBootstrapReadResult>[] results) : IPersonalBootstrapReader
    {
        private int _index;

        public Task<PersonalBootstrapReadResult> ReadAsync(CancellationToken cancellationToken = default)
        {
            var index = Interlocked.Increment(ref _index) - 1;
            return results[index];
        }
    }

    private sealed class RecordingObservationsReader : IStudentRecentObservationsReader
    {
        public int CallCount { get; private set; }
        public StudentObservationScope? LastScope { get; private set; }
        public Guid LastActorId { get; private set; }

        public Task<StudentRecentObservationsReadResult> ReadAsync(
            StudentObservationScope scope,
            Guid expectedActorAppUserId,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            LastScope = scope;
            LastActorId = expectedActorAppUserId;
            return Task.FromResult(StudentRecentObservationsReadResult.Success(
                new StudentRecentObservationsSnapshot(
                    DateTimeOffset.Parse("2026-09-17T12:01:00Z"),
                    expectedActorAppUserId,
                    scope.OrganizationId,
                    scope.StudentId,
                    "虚构学生0001",
                    scope.SubjectProfileId,
                    "chinese",
                    Guid.Parse("50000000-0000-0000-0000-000000000001"),
                    new[]
                    {
                        new StudentRecentObservation(
                            Guid.Parse("81000000-0000-0000-0000-000000000001"),
                            expectedActorAppUserId,
                            "虚构课堂观察",
                            null,
                            DateTimeOffset.Parse("2026-09-17T12:00:30Z")),
                    },
                    false)));
        }
    }
}

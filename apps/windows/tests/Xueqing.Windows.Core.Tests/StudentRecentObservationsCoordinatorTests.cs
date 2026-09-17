using Microsoft.VisualStudio.TestTools.UnitTesting;
using Xueqing.Windows.Core.Models;
using Xueqing.Windows.Core.Services;

namespace Xueqing.Windows.Core.Tests;

[TestClass]
public sealed class StudentRecentObservationsCoordinatorTests
{
    private static readonly Guid ActorId = Guid.Parse("10000000-0000-0000-0000-000000000001");

    [TestMethod]
    public async Task Newer_context_wins_when_older_request_finishes_last()
    {
        var firstCompletion = new TaskCompletionSource<StudentRecentObservationsReadResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondCompletion = new TaskCompletionSource<StudentRecentObservationsReadResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var reader = new SequencedReader(firstCompletion.Task, secondCompletion.Task);
        var coordinator = new StudentRecentObservationsCoordinator(reader);
        var firstScope = Scope("30000000-0000-0000-0000-000000000001");
        var secondScope = Scope("30000000-0000-0000-0000-000000000002");

        var firstLoad = coordinator.LoadAsync(firstScope, ActorId);
        var secondLoad = coordinator.LoadAsync(secondScope, ActorId);

        secondCompletion.SetResult(StudentRecentObservationsReadResult.Success(Snapshot(secondScope, "第二位学生")));
        await secondLoad;
        firstCompletion.SetResult(StudentRecentObservationsReadResult.Success(Snapshot(firstScope, "第一位学生")));
        await firstLoad;

        Assert.AreEqual(secondScope, coordinator.Current.Scope);
        Assert.AreEqual("第二位学生", coordinator.Current.Snapshot?.StudentDisplayName);
        Assert.AreEqual(StudentRecentObservationsViewStatus.Data, coordinator.Current.Status);
        Assert.AreEqual(2L, coordinator.Current.Generation);
    }

    [TestMethod]
    public async Task Empty_history_is_distinct_from_failure()
    {
        var scope = Scope("30000000-0000-0000-0000-000000000001");
        var reader = new ImmediateReader(StudentRecentObservationsReadResult.Success(Snapshot(scope, "学生", Array.Empty<StudentRecentObservation>())));
        var coordinator = new StudentRecentObservationsCoordinator(reader);

        var state = await coordinator.LoadAsync(scope, ActorId);

        Assert.AreEqual(StudentRecentObservationsViewStatus.Empty, state.Status);
        Assert.IsNotNull(state.Snapshot);
        Assert.IsNull(state.FailureCode);
    }

    [TestMethod]
    public async Task Access_failure_clears_visible_facts_instead_of_showing_stale_data()
    {
        var scope = Scope("30000000-0000-0000-0000-000000000001");
        var reader = new SequencedReader(
            Task.FromResult(StudentRecentObservationsReadResult.Success(Snapshot(scope, "学生"))),
            Task.FromResult(StudentRecentObservationsReadResult.Failed(
                StudentRecentObservationsFailureKind.AccessDenied,
                "XQ_TEACHING_CONTEXT_UNAVAILABLE")));
        var coordinator = new StudentRecentObservationsCoordinator(reader);

        await coordinator.LoadAsync(scope, ActorId);
        var denied = await coordinator.LoadAsync(scope, ActorId);

        Assert.AreEqual(StudentRecentObservationsViewStatus.AccessDenied, denied.Status);
        Assert.IsNull(denied.Snapshot);
        Assert.AreEqual("XQ_TEACHING_CONTEXT_UNAVAILABLE", denied.FailureCode);
    }

    [TestMethod]
    public async Task Reset_invalidates_in_flight_result()
    {
        var completion = new TaskCompletionSource<StudentRecentObservationsReadResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var reader = new ImmediateTaskReader(completion.Task);
        var coordinator = new StudentRecentObservationsCoordinator(reader);
        var scope = Scope("30000000-0000-0000-0000-000000000001");

        var loading = coordinator.LoadAsync(scope, ActorId);
        coordinator.Reset();
        completion.SetResult(StudentRecentObservationsReadResult.Success(Snapshot(scope, "旧上下文")));
        await loading;

        Assert.AreEqual(StudentRecentObservationsViewStatus.Idle, coordinator.Current.Status);
        Assert.IsNull(coordinator.Current.Scope);
        Assert.IsNull(coordinator.Current.Snapshot);
    }

    private static StudentObservationScope Scope(string studentId) =>
        new(
            Guid.Parse("20000000-0000-0000-0000-000000000001"),
            Guid.Parse(studentId),
            Guid.Parse("40000000-0000-0000-0000-000000000001"));

    private static StudentRecentObservationsSnapshot Snapshot(
        StudentObservationScope scope,
        string displayName,
        IReadOnlyList<StudentRecentObservation>? observations = null) =>
        new(
            DateTimeOffset.Parse("2026-09-17T12:01:00Z"),
            ActorId,
            scope.OrganizationId,
            scope.StudentId,
            displayName,
            scope.SubjectProfileId,
            "chinese",
            Guid.Parse("50000000-0000-0000-0000-000000000001"),
            observations ?? new[]
            {
                new StudentRecentObservation(
                    Guid.Parse("81000000-0000-0000-0000-000000000001"),
                    ActorId,
                    "虚构课堂记录",
                    null,
                    DateTimeOffset.Parse("2026-09-17T12:00:00Z")),
            },
            false);

    private sealed class ImmediateReader(StudentRecentObservationsReadResult result) : IStudentRecentObservationsReader
    {
        public Task<StudentRecentObservationsReadResult> ReadAsync(
            StudentObservationScope scope,
            Guid expectedActorAppUserId,
            CancellationToken cancellationToken = default) => Task.FromResult(result);
    }

    private sealed class ImmediateTaskReader(Task<StudentRecentObservationsReadResult> task) : IStudentRecentObservationsReader
    {
        public Task<StudentRecentObservationsReadResult> ReadAsync(
            StudentObservationScope scope,
            Guid expectedActorAppUserId,
            CancellationToken cancellationToken = default) => task;
    }

    private sealed class SequencedReader(params Task<StudentRecentObservationsReadResult>[] results) : IStudentRecentObservationsReader
    {
        private int _index;

        public Task<StudentRecentObservationsReadResult> ReadAsync(
            StudentObservationScope scope,
            Guid expectedActorAppUserId,
            CancellationToken cancellationToken = default)
        {
            var index = Interlocked.Increment(ref _index) - 1;
            return results[index];
        }
    }
}

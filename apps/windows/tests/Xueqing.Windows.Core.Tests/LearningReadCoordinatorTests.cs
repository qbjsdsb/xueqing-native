using Microsoft.VisualStudio.TestTools.UnitTesting;
using Xueqing.Windows.Core.Models;
using Xueqing.Windows.Core.Services;

namespace Xueqing.Windows.Core.Tests;

[TestClass]
public sealed class LearningReadCoordinatorTests
{
    private static readonly Guid ActorId = Guid.Parse("10000000-0000-0000-0000-000000000001");

    [TestMethod]
    public async Task Focus_newer_scope_wins_when_older_request_finishes_last()
    {
        var firstCompletion = new TaskCompletionSource<StudentLearningFocusReadResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var secondCompletion = new TaskCompletionSource<StudentLearningFocusReadResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var reader = new SequencedFocusReader(firstCompletion.Task, secondCompletion.Task);
        var coordinator = new StudentLearningFocusCoordinator(reader);
        var firstScope = Scope("30000000-0000-0000-0000-000000000001");
        var secondScope = Scope("30000000-0000-0000-0000-000000000002");

        var firstLoad = coordinator.LoadAsync(firstScope, ActorId);
        var secondLoad = coordinator.LoadAsync(secondScope, ActorId);

        secondCompletion.SetResult(StudentLearningFocusReadResult.Success(
            FocusSnapshot(secondScope, "第二位学生")));
        await secondLoad;
        firstCompletion.SetResult(StudentLearningFocusReadResult.Success(
            FocusSnapshot(firstScope, "第一位学生")));
        await firstLoad;

        Assert.AreEqual(secondScope, coordinator.Current.Scope);
        Assert.AreEqual("第二位学生", coordinator.Current.Snapshot?.StudentDisplayName);
        Assert.AreEqual(StudentLearningFocusViewStatus.Data, coordinator.Current.Status);
        Assert.AreEqual(2L, coordinator.Current.Generation);
    }

    [TestMethod]
    public async Task Focus_access_or_invariant_failure_clears_previous_snapshot()
    {
        var scope = Scope("30000000-0000-0000-0000-000000000001");
        var reader = new SequencedFocusReader(
            Task.FromResult(StudentLearningFocusReadResult.Success(FocusSnapshot(scope, "学生"))),
            Task.FromResult(StudentLearningFocusReadResult.Failed(
                LearningReadFailureKind.ServerInvariant,
                "XQ_CASE_PRIMARY_ACTION_INVARIANT")));
        var coordinator = new StudentLearningFocusCoordinator(reader);

        await coordinator.LoadAsync(scope, ActorId);
        var failed = await coordinator.LoadAsync(scope, ActorId);

        Assert.AreEqual(StudentLearningFocusViewStatus.ServerInvariant, failed.Status);
        Assert.IsNull(failed.Snapshot);
        Assert.AreEqual("XQ_CASE_PRIMARY_ACTION_INVARIANT", failed.FailureCode);
    }

    [TestMethod]
    public async Task Focus_reset_invalidates_in_flight_result()
    {
        var completion = new TaskCompletionSource<StudentLearningFocusReadResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var coordinator = new StudentLearningFocusCoordinator(
            new ImmediateFocusTaskReader(completion.Task));
        var scope = Scope("30000000-0000-0000-0000-000000000001");

        var loading = coordinator.LoadAsync(scope, ActorId);
        coordinator.Reset();
        completion.SetResult(StudentLearningFocusReadResult.Success(
            FocusSnapshot(scope, "旧上下文")));
        await loading;

        Assert.AreEqual(StudentLearningFocusViewStatus.Idle, coordinator.Current.Status);
        Assert.IsNull(coordinator.Current.Scope);
        Assert.IsNull(coordinator.Current.Snapshot);
    }

    [TestMethod]
    public async Task Today_empty_is_distinct_from_failure()
    {
        var empty = new PersonalTodayActionsSnapshot(
            DateTimeOffset.Parse("2026-09-19T06:00:00Z"),
            ActorId,
            Array.Empty<PersonalTodayAction>(),
            false);
        var coordinator = new PersonalTodayActionsCoordinator(
            new ImmediateTodayReader(PersonalTodayActionsReadResult.Success(empty)));

        var state = await coordinator.LoadAsync(ActorId);

        Assert.AreEqual(PersonalTodayActionsViewStatus.Empty, state.Status);
        Assert.IsNotNull(state.Snapshot);
        Assert.IsNull(state.FailureCode);
    }

    [TestMethod]
    public async Task Today_newer_refresh_wins_and_failure_never_leaves_stale_actions()
    {
        var firstCompletion = new TaskCompletionSource<PersonalTodayActionsReadResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var secondCompletion = new TaskCompletionSource<PersonalTodayActionsReadResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var reader = new SequencedTodayReader(firstCompletion.Task, secondCompletion.Task);
        var coordinator = new PersonalTodayActionsCoordinator(reader);

        var firstLoad = coordinator.LoadAsync(ActorId);
        var secondLoad = coordinator.LoadAsync(ActorId);

        secondCompletion.SetResult(PersonalTodayActionsReadResult.Failed(
            LearningReadFailureKind.AccessDenied,
            "XQ_ACTOR_DISABLED"));
        await secondLoad;
        firstCompletion.SetResult(PersonalTodayActionsReadResult.Success(TodaySnapshot()));
        await firstLoad;

        Assert.AreEqual(PersonalTodayActionsViewStatus.AccessDenied, coordinator.Current.Status);
        Assert.IsNull(coordinator.Current.Snapshot);
        Assert.AreEqual("XQ_ACTOR_DISABLED", coordinator.Current.FailureCode);
        Assert.AreEqual(2L, coordinator.Current.Generation);
    }

    [TestMethod]
    public async Task Today_reset_invalidates_in_flight_result()
    {
        var completion = new TaskCompletionSource<PersonalTodayActionsReadResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var coordinator = new PersonalTodayActionsCoordinator(
            new ImmediateTodayTaskReader(completion.Task));

        var loading = coordinator.LoadAsync(ActorId);
        coordinator.Reset();
        completion.SetResult(PersonalTodayActionsReadResult.Success(TodaySnapshot()));
        await loading;

        Assert.AreEqual(PersonalTodayActionsViewStatus.Idle, coordinator.Current.Status);
        Assert.IsNull(coordinator.Current.Snapshot);
    }

    private static StudentLearningScope Scope(string studentId) =>
        new(
            Guid.Parse("20000000-0000-0000-0000-000000000001"),
            Guid.Parse(studentId),
            Guid.Parse("40000000-0000-0000-0000-000000000001"));

    private static StudentLearningFocusSnapshot FocusSnapshot(
        StudentLearningScope scope,
        string displayName) =>
        new(
            DateTimeOffset.Parse("2026-09-19T06:00:00Z"),
            ActorId,
            scope.OrganizationId,
            "虚构机构",
            "Asia/Shanghai",
            new DateOnly(2026, 9, 19),
            scope.StudentId,
            displayName,
            scope.SubjectProfileId,
            "chinese",
            Guid.Parse("50000000-0000-0000-0000-000000000001"),
            new[]
            {
                new StudentLearningCaseFocus(
                    Guid.Parse("61000000-0000-0000-0000-000000000001"),
                    "虚构学情问题",
                    LearningCaseState.New,
                    1,
                    ActorId,
                    Guid.Parse("50000000-0000-0000-0000-000000000001"),
                    DateTimeOffset.Parse("2026-09-19T04:00:00Z"),
                    DateTimeOffset.Parse("2026-09-19T05:00:00Z"),
                    new LearningPrimaryAction(
                        Guid.Parse("62000000-0000-0000-0000-000000000001"),
                        "虚构下一步行动",
                        new DateOnly(2026, 9, 19),
                        ActionDueBucket.Today,
                        1)),
            },
            false);

    private static PersonalTodayActionsSnapshot TodaySnapshot() =>
        new(
            DateTimeOffset.Parse("2026-09-19T06:00:00Z"),
            ActorId,
            new[]
            {
                new PersonalTodayAction(
                    Guid.Parse("20000000-0000-0000-0000-000000000001"),
                    "虚构机构",
                    "Asia/Shanghai",
                    new DateOnly(2026, 9, 19),
                    Guid.Parse("30000000-0000-0000-0000-000000000001"),
                    "虚构学生",
                    Guid.Parse("40000000-0000-0000-0000-000000000001"),
                    "chinese",
                    Guid.Parse("50000000-0000-0000-0000-000000000001"),
                    Guid.Parse("61000000-0000-0000-0000-000000000001"),
                    "虚构学情问题",
                    LearningCaseState.New,
                    1,
                    Guid.Parse("62000000-0000-0000-0000-000000000001"),
                    "虚构下一步行动",
                    new DateOnly(2026, 9, 19),
                    ActionDueBucket.Today,
                    1,
                    DateTimeOffset.Parse("2026-09-19T05:00:00Z")),
            },
            false);

    private sealed class ImmediateTodayReader(PersonalTodayActionsReadResult result) : IPersonalTodayActionsReader
    {
        public Task<PersonalTodayActionsReadResult> ReadAsync(
            Guid expectedActorAppUserId,
            CancellationToken cancellationToken = default) => Task.FromResult(result);
    }

    private sealed class ImmediateTodayTaskReader(Task<PersonalTodayActionsReadResult> task) : IPersonalTodayActionsReader
    {
        public Task<PersonalTodayActionsReadResult> ReadAsync(
            Guid expectedActorAppUserId,
            CancellationToken cancellationToken = default) => task;
    }

    private sealed class SequencedTodayReader(params Task<PersonalTodayActionsReadResult>[] results) : IPersonalTodayActionsReader
    {
        private int _index;

        public Task<PersonalTodayActionsReadResult> ReadAsync(
            Guid expectedActorAppUserId,
            CancellationToken cancellationToken = default)
        {
            var index = Interlocked.Increment(ref _index) - 1;
            return results[index];
        }
    }

    private sealed class ImmediateFocusTaskReader(Task<StudentLearningFocusReadResult> task) : IStudentLearningFocusReader
    {
        public Task<StudentLearningFocusReadResult> ReadAsync(
            StudentLearningScope scope,
            Guid expectedActorAppUserId,
            CancellationToken cancellationToken = default) => task;
    }

    private sealed class SequencedFocusReader(params Task<StudentLearningFocusReadResult>[] results) : IStudentLearningFocusReader
    {
        private int _index;

        public Task<StudentLearningFocusReadResult> ReadAsync(
            StudentLearningScope scope,
            Guid expectedActorAppUserId,
            CancellationToken cancellationToken = default)
        {
            var index = Interlocked.Increment(ref _index) - 1;
            return results[index];
        }
    }
}

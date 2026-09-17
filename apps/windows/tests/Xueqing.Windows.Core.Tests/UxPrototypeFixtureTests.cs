using Microsoft.VisualStudio.TestTools.UnitTesting;
using Xueqing.Windows.Core.Models;
using Xueqing.Windows.Core.Services;

namespace Xueqing.Windows.Core.Tests;

[TestClass]
public sealed class UxPrototypeFixtureTests
{
    [TestMethod]
    public void Today_fixture_has_one_bucket_per_action_and_all_hostile_states()
    {
        var actions = UxPrototypeFixtureFactory.CreateTodayActions();

        Assert.AreEqual(28, actions.Count);
        Assert.AreEqual(28, actions.Select(action => action.Id).Distinct().Count());
        Assert.AreEqual(5, actions.Count(action => action.StudentId == "student-000007"));
        CollectionAssert.AreEquivalent(
            Enum.GetValues<TodayActionBucket>(),
            actions.Select(action => action.Bucket).Distinct().ToArray());
        CollectionAssert.AreEquivalent(
            Enum.GetValues<PrototypeSaveState>(),
            actions.Select(action => action.SaveState).Distinct().ToArray());
        Assert.IsTrue(actions.Any(action => action.DueDate is null));
        Assert.IsTrue(actions.Any(action => action.Title.Length > 40));
    }

    [TestMethod]
    public void Learning_case_fixture_is_long_deterministic_and_keeps_lifecycle_history()
    {
        var first = UxPrototypeFixtureFactory.CreateLearningCase();
        var second = UxPrototypeFixtureFactory.CreateLearningCase();

        Assert.AreEqual(first, second);
        Assert.AreEqual(100, first.Timeline.Count);
        Assert.IsTrue(first.Timeline.Any(entry => entry.Kind == CaseTimelineEntryKind.Lifecycle && entry.Heading == "重新打开"));
        Assert.IsTrue(first.Timeline.Any(entry => entry.Body.Length > 60));
        Assert.IsFalse(string.IsNullOrWhiteSpace(first.NextAction));
    }

    [TestMethod]
    public void Organization_fixture_exercises_dense_and_degraded_rows()
    {
        var rows = UxPrototypeFixtureFactory.CreateOrganizationMembers();

        Assert.AreEqual(80, rows.Count);
        Assert.AreEqual(80, rows.Select(row => row.Id).Distinct().Count());
        Assert.IsTrue(rows.Any(row => row.DisplayName.Length > 12));
        Assert.IsTrue(rows.Any(row => row.RoleLabel.Contains("负责人", StringComparison.Ordinal)));
        Assert.IsTrue(rows.Any(row => row.StatusLabel == "已停用"));
        Assert.IsTrue(rows.Any(row => row.CanUseBulkSafeAction));
        Assert.IsTrue(rows.Any(row => !row.CanUseBulkSafeAction));
    }
}

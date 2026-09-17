using Microsoft.VisualStudio.TestTools.UnitTesting;
using Xueqing.Windows.Core.Layout;
using Xueqing.Windows.Core.Models;
using Xueqing.Windows.Core.Services;

namespace Xueqing.Windows.Core.Tests;

[TestClass]
public sealed class UxPrototypeFixtureTests
{
    [TestMethod]
    public void Today_fixture_has_one_bucket_per_action_and_all_interaction_contract_states()
    {
        var actions = UxPrototypeFixtureFactory.CreateTodayActions();

        Assert.AreEqual(28, actions.Count);
        Assert.AreEqual(28, actions.Select(action => action.Id).Distinct().Count());
        Assert.AreEqual(5, actions.Count(action => action.StudentId == "student-000007"));
        CollectionAssert.AreEquivalent(
            Enum.GetValues<TodayActionBucket>(),
            actions.Select(action => action.Bucket).Distinct().ToArray());
        CollectionAssert.AreEquivalent(
            Enum.GetValues<PrototypeInteractionState>(),
            actions.Select(action => action.InteractionState).Distinct().ToArray());
        Assert.IsTrue(actions.Any(action => action.DueDate is null));
        Assert.IsTrue(actions.Any(action => action.Title.Length > 40));

        var labels = actions.Select(action => action.InteractionStateLabel).ToArray();
        Assert.IsFalse(labels.Any(label => label.Contains("保存成功", StringComparison.Ordinal)));
        Assert.IsFalse(labels.Any(label => label.Contains("本地版本", StringComparison.Ordinal)));
        Assert.IsFalse(labels.Any(label => label.Contains("云端版本", StringComparison.Ordinal)));

        var committed = actions.First(action => action.InteractionState == PrototypeInteractionState.Committed);
        Assert.IsTrue(committed.AuthoritativeCompletionKnown);
        Assert.AreEqual(string.Empty, committed.InteractionStateLabel, "Normal authoritative success should stay quiet.");

        var localDraft = actions.First(action => action.InteractionState == PrototypeInteractionState.LocalDraftSafe);
        Assert.IsFalse(localDraft.AuthoritativeCompletionKnown);
        Assert.IsTrue(localDraft.PreservesRecoverableLocalContent);
        StringAssert.Contains(localDraft.InteractionStateLabel, "本机");
        StringAssert.Contains(localDraft.InteractionStateLabel, "尚未提交");

        var unknown = actions.First(action => action.InteractionState == PrototypeInteractionState.ResultUnknown);
        Assert.IsTrue(unknown.RequiresSameOperationIdentity);
        Assert.IsFalse(unknown.AuthoritativeCompletionKnown);
        Assert.IsTrue(unknown.PreservesRecoverableLocalContent);
        StringAssert.Contains(unknown.InteractionStateLabel, "确认结果");

        var rejected = actions.First(action => action.InteractionState == PrototypeInteractionState.ServerRejected);
        Assert.IsTrue(rejected.PreservesRecoverableLocalContent);
        StringAssert.Contains(rejected.InteractionStateLabel, "本机内容仍保留");

        var conflict = actions.First(action => action.InteractionState == PrototypeInteractionState.VersionConflict);
        Assert.IsTrue(conflict.PreservesRecoverableLocalContent);
        StringAssert.Contains(conflict.InteractionStateLabel, "本机草稿仍保留");

        var permissionReduced = actions.First(action => action.InteractionState == PrototypeInteractionState.PermissionReduced);
        StringAssert.Contains(permissionReduced.InteractionStateLabel, "无法继续提交");

        var offlineAllowed = actions.First(action => action.InteractionState == PrototypeInteractionState.OfflineAccessAllowed);
        var offlineUnavailable = actions.First(action => action.InteractionState == PrototypeInteractionState.OfflineAccessUnavailable);
        Assert.AreNotEqual(offlineAllowed.InteractionStateLabel, offlineUnavailable.InteractionStateLabel);
    }

    [TestMethod]
    public void Learning_case_fixture_is_long_deterministic_and_keeps_lifecycle_history()
    {
        var first = UxPrototypeFixtureFactory.CreateLearningCase();
        var second = UxPrototypeFixtureFactory.CreateLearningCase();

        Assert.AreEqual(first.Id, second.Id);
        Assert.AreEqual(first.Title, second.Title);
        Assert.AreEqual(first.StateLabel, second.StateLabel);
        Assert.AreEqual(first.NextAction, second.NextAction);
        Assert.AreEqual(100, first.Timeline.Count);
        CollectionAssert.AreEqual(first.Timeline.ToArray(), second.Timeline.ToArray());
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

    [TestMethod]
    public void Student_fixture_exercises_large_list_long_names_and_case_count_pressure()
    {
        var students = SyntheticDataFactory.CreateStudents(1_000);

        Assert.AreEqual(1_000, students.Count);
        Assert.AreEqual(1_000, students.Select(student => student.Id).Distinct().Count());
        Assert.IsTrue(students.Any(student => student.DisplayName.Length > 20));
        Assert.IsTrue(students.Any(student => student.ActiveCaseCount == 0));
        Assert.IsTrue(students.Any(student => student.ActiveCaseCount == 1));
        Assert.IsTrue(students.Any(student => student.ActiveCaseCount >= 20));
        CollectionAssert.AreEquivalent(
            new[] { "语文", "数学", "英语", "物理" },
            students.Select(student => student.PrimarySubject).Distinct().ToArray());
    }

    [TestMethod]
    public void Student_search_filters_one_thousand_rows_without_changing_source_order()
    {
        var students = SyntheticDataFactory.CreateStudents(1_000);

        var byName = StudentSearch.Filter(students, "虚构学生0100");
        Assert.AreEqual(1, byName.Count);
        Assert.AreEqual("student-000100", byName[0].Id);

        var byCode = StudentSearch.Filter(students, "S000777");
        Assert.AreEqual(1, byCode.Count);
        Assert.AreEqual("student-000777", byCode[0].Id);

        var bySubject = StudentSearch.Filter(students, "语文");
        Assert.AreEqual(250, bySubject.Count);
        Assert.IsTrue(bySubject.All(student => student.PrimarySubject == "语文"));
        Assert.IsTrue(bySubject.Zip(bySubject.Skip(1), (left, right) => StringComparer.Ordinal.Compare(left.Id, right.Id) < 0).All(value => value));

        var reset = StudentSearch.Filter(students, "   ");
        Assert.AreEqual(1_000, reset.Count);
        CollectionAssert.AreEqual(students.ToArray(), reset.ToArray());
    }

    [TestMethod]
    public void Window_layout_policy_covers_required_native_validation_widths()
    {
        var matrix = new Dictionary<double, WindowLayoutMode>
        {
            [800] = WindowLayoutMode.Compact,
            [960] = WindowLayoutMode.Standard,
            [1024] = WindowLayoutMode.Standard,
            [1280] = WindowLayoutMode.Expanded,
            [1600] = WindowLayoutMode.Expanded,
        };

        foreach (var (width, expected) in matrix)
        {
            Assert.AreEqual(expected, WindowLayoutPolicy.Resolve(width), $"Unexpected layout at {width} DIP.");
        }

        Assert.AreEqual(WindowLayoutMode.Compact, WindowLayoutPolicy.Resolve(WindowLayoutPolicy.CompactBreakpoint - 0.01));
        Assert.AreEqual(WindowLayoutMode.Standard, WindowLayoutPolicy.Resolve(WindowLayoutPolicy.CompactBreakpoint));
        Assert.AreEqual(WindowLayoutMode.Standard, WindowLayoutPolicy.Resolve(WindowLayoutPolicy.ExpandedBreakpoint - 0.01));
        Assert.AreEqual(WindowLayoutMode.Expanded, WindowLayoutPolicy.Resolve(WindowLayoutPolicy.ExpandedBreakpoint));
    }
}

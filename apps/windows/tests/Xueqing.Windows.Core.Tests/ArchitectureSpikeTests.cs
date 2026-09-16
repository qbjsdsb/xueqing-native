using Microsoft.VisualStudio.TestTools.UnitTesting;
using Xueqing.Windows.Core.Layout;
using Xueqing.Windows.Core.Services;

namespace Xueqing.Windows.Core.Tests;

[TestClass]
public sealed class ArchitectureSpikeTests
{
    [TestMethod]
    [DataRow(899.9, WindowLayoutMode.Compact)]
    [DataRow(900.0, WindowLayoutMode.Standard)]
    [DataRow(1279.9, WindowLayoutMode.Standard)]
    [DataRow(1280.0, WindowLayoutMode.Expanded)]
    [DataRow(1920.0, WindowLayoutMode.Expanded)]
    public void Layout_breakpoints_are_single_source(double width, WindowLayoutMode expected)
    {
        Assert.AreEqual(expected, WindowLayoutPolicy.Resolve(width));
    }

    [TestMethod]
    public void Synthetic_students_are_deterministic_and_large_enough_for_spike()
    {
        var first = SyntheticDataFactory.CreateStudents(1_000);
        var second = SyntheticDataFactory.CreateStudents(1_000);

        Assert.AreEqual(1_000, first.Count);
        CollectionAssert.AreEqual(first.ToArray(), second.ToArray());
        Assert.AreEqual("student-001000", first[^1].Id);
        Assert.AreEqual("虚构学生1000", first[^1].DisplayName);
    }
}

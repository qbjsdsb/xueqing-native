using Microsoft.VisualStudio.TestTools.UnitTesting;
using Xueqing.Windows.ViewModels;

namespace Xueqing.Windows.Core.Tests;

[TestClass]
public sealed class MainWindowStartupModeTests
{
    [TestMethod]
    public void Prototype_mode_is_explicit_and_contains_fixture_state()
    {
        var viewModel = new MainWindowViewModel();

        Assert.AreEqual(MainWindowStartupMode.Prototype, viewModel.StartupMode);
        Assert.IsTrue(viewModel.IsPrototypeMode);
        Assert.IsFalse(viewModel.IsStartupUnavailable);
        Assert.AreEqual(1_000, viewModel.Students.Count);
        Assert.IsTrue(viewModel.TodayActions.Count > 0);
        Assert.IsTrue(viewModel.CanUseOrganizationWorkspace);
    }

    [TestMethod]
    public void Signed_out_mode_never_falls_back_to_fixture_data()
    {
        var viewModel = MainWindowViewModel.CreateSignedOut();

        Assert.AreEqual(MainWindowStartupMode.SignedOut, viewModel.StartupMode);
        Assert.IsFalse(viewModel.IsPrototypeMode);
        Assert.IsTrue(viewModel.IsStartupUnavailable);
        Assert.AreEqual(0, viewModel.Students.Count);
        Assert.AreEqual(0, viewModel.TodayActions.Count);
        Assert.AreEqual(0, viewModel.OrganizationMembers.Count);
        Assert.IsFalse(viewModel.CanUseOrganizationWorkspace);
        Assert.IsFalse(viewModel.IsOrganizationManagementAuthoritative);
        StringAssert.Contains(viewModel.StartupStatusText, "登录");
    }

    [TestMethod]
    public async Task Configuration_unavailable_mode_stays_fail_closed_after_initialize()
    {
        var viewModel = MainWindowViewModel.CreateConfigurationUnavailable(
            "fictional configuration unavailable");

        await viewModel.InitializeAsync();

        Assert.AreEqual(MainWindowStartupMode.ConfigurationUnavailable, viewModel.StartupMode);
        Assert.IsTrue(viewModel.IsStartupUnavailable);
        Assert.AreEqual(0, viewModel.Students.Count);
        Assert.AreEqual(0, viewModel.TodayActions.Count);
        Assert.AreEqual("fictional configuration unavailable", viewModel.StartupStatusText);
        Assert.AreEqual(
            "fictional configuration unavailable",
            viewModel.OrganizationManagementStatusText);
    }
}

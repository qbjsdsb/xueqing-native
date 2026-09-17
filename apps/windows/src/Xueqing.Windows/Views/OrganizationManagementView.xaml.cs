using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Xueqing.Windows.Core.Layout;

namespace Xueqing.Windows.Views;

public sealed partial class OrganizationManagementView : UserControl
{
    public OrganizationManagementView()
    {
        InitializeComponent();
    }

    public void ApplyLayout(WindowLayoutMode mode)
    {
        var useCompactRows = mode != WindowLayoutMode.Expanded;
        WideList.Visibility = useCompactRows ? Visibility.Collapsed : Visibility.Visible;
        CompactList.Visibility = useCompactRows ? Visibility.Visible : Visibility.Collapsed;
    }
}

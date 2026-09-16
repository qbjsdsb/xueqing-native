using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Xueqing.Windows.Core.Models;
using Xueqing.Windows.Core.Services;

namespace Xueqing.Windows.ViewModels;

public sealed class MainWindowViewModel : ObservableObject
{
    private StudentSummary _selectedStudent;

    public MainWindowViewModel()
    {
        Students = new ObservableCollection<StudentSummary>(SyntheticDataFactory.CreateStudents(1_000));
        _selectedStudent = Students[0];
    }

    public ObservableCollection<StudentSummary> Students { get; }

    public StudentSummary SelectedStudent
    {
        get => _selectedStudent;
        set => SetProperty(ref _selectedStudent, value);
    }
}

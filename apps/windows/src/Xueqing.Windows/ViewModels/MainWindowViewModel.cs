using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Xueqing.Windows.Core.Models;
using Xueqing.Windows.Core.Services;

namespace Xueqing.Windows.ViewModels;

public sealed class MainWindowViewModel : ObservableObject
{
    private readonly IReadOnlyList<StudentSummary> _allStudents;
    private StudentSummary? _selectedStudent;

    public MainWindowViewModel()
    {
        _allStudents = SyntheticDataFactory.CreateStudents(1_000);
        Students = new ObservableCollection<StudentSummary>(_allStudents);
        TodayActions = new ObservableCollection<TodayActionItem>(UxPrototypeFixtureFactory.CreateTodayActions());
        OrganizationMembers = new ObservableCollection<OrganizationMemberRow>(UxPrototypeFixtureFactory.CreateOrganizationMembers());
        LearningCase = UxPrototypeFixtureFactory.CreateLearningCase();
        _selectedStudent = Students[0];
    }

    public ObservableCollection<StudentSummary> Students { get; }

    public ObservableCollection<TodayActionItem> TodayActions { get; }

    public LearningCasePrototype LearningCase { get; }

    public ObservableCollection<OrganizationMemberRow> OrganizationMembers { get; }

    public StudentSummary? SelectedStudent
    {
        get => _selectedStudent;
        set => SetProperty(ref _selectedStudent, value);
    }

    public void FilterStudents(string? query)
    {
        var selectedId = SelectedStudent?.Id;
        var filtered = StudentSearch.Filter(_allStudents, query);

        Students.Clear();
        foreach (var student in filtered)
        {
            Students.Add(student);
        }

        if (selectedId is not null)
        {
            var retained = filtered.FirstOrDefault(student => student.Id == selectedId);
            if (retained is not null)
            {
                SelectedStudent = retained;
                return;
            }
        }

        SelectedStudent = filtered.Count > 0 ? filtered[0] : null;
    }
}

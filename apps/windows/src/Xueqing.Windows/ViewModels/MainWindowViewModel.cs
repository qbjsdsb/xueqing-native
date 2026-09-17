using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml;
using Xueqing.Windows.Core.Models;
using Xueqing.Windows.Core.Services;

namespace Xueqing.Windows.ViewModels;

public sealed record TeachingContextOption(
    PersonalTeachingContext Context,
    string DisplayName);

public sealed class MainWindowViewModel : ObservableObject
{
    private readonly PersonalStudentWorkspaceCoordinator? _personalWorkspace;
    private readonly List<StudentSummary> _allStudents;
    private readonly Dictionary<string, PersonalStudentWorkspaceItem> _authoritativeStudents = new(StringComparer.Ordinal);
    private StudentSummary? _selectedStudent;
    private TeachingContextOption? _selectedTeachingContext;
    private string _recentObservationsStatusText = string.Empty;
    private string _recentHistoryMoreText = string.Empty;
    private bool _initialized;

    public MainWindowViewModel()
        : this(null)
    {
    }

    public MainWindowViewModel(PersonalStudentWorkspaceCoordinator? personalWorkspace)
    {
        _personalWorkspace = personalWorkspace;
        _allStudents = personalWorkspace is null
            ? SyntheticDataFactory.CreateStudents(1_000).ToList()
            : new List<StudentSummary>();
        Students = new ObservableCollection<StudentSummary>(_allStudents);
        SelectedTeachingContexts = new ObservableCollection<TeachingContextOption>();
        RecentObservations = new ObservableCollection<StudentRecentObservation>();
        TodayActions = new ObservableCollection<TodayActionItem>(UxPrototypeFixtureFactory.CreateTodayActions());
        OrganizationMembers = new ObservableCollection<OrganizationMemberRow>(UxPrototypeFixtureFactory.CreateOrganizationMembers());
        LearningCase = UxPrototypeFixtureFactory.CreateLearningCase();
        _selectedStudent = Students.FirstOrDefault();

        if (personalWorkspace is null)
        {
            _recentObservationsStatusText = "UX 原型记录，仅用于布局与交互验证。";
        }
        else
        {
            _recentObservationsStatusText = "正在准备当前任教学员…";
        }
    }

    public ObservableCollection<StudentSummary> Students { get; }

    public ObservableCollection<TodayActionItem> TodayActions { get; }

    public LearningCasePrototype LearningCase { get; }

    public ObservableCollection<OrganizationMemberRow> OrganizationMembers { get; }

    public ObservableCollection<TeachingContextOption> SelectedTeachingContexts { get; }

    public ObservableCollection<StudentRecentObservation> RecentObservations { get; }

    public bool IsAuthoritativeStudentWorkspace => _personalWorkspace is not null;

    public Visibility PrototypeStudentDetailVisibility => IsAuthoritativeStudentWorkspace
        ? Visibility.Collapsed
        : Visibility.Visible;

    public Visibility AuthoritativeStudentDetailVisibility => IsAuthoritativeStudentWorkspace
        ? Visibility.Visible
        : Visibility.Collapsed;

    public string SearchPlaceholderText => IsAuthoritativeStudentWorkspace
        ? "搜索姓名或学科"
        : "搜索姓名、学号或学科";

    public StudentSummary? SelectedStudent
    {
        get => _selectedStudent;
        set
        {
            if (SetProperty(ref _selectedStudent, value) && IsAuthoritativeStudentWorkspace)
            {
                PrepareTeachingContexts(value);
            }
        }
    }

    public TeachingContextOption? SelectedTeachingContext
    {
        get => _selectedTeachingContext;
        set
        {
            if (SetProperty(ref _selectedTeachingContext, value))
            {
                RecentObservations.Clear();
                RecentHistoryMoreText = string.Empty;
                RecentObservationsStatusText = value is null
                    ? (SelectedTeachingContexts.Count > 1 ? "选择学科后读取最近记录。" : "暂无可读取的教学上下文。")
                    : "可读取最近记录。";
            }
        }
    }

    public string RecentObservationsStatusText
    {
        get => _recentObservationsStatusText;
        private set => SetProperty(ref _recentObservationsStatusText, value);
    }

    public string RecentHistoryMoreText
    {
        get => _recentHistoryMoreText;
        private set => SetProperty(ref _recentHistoryMoreText, value);
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;
        if (_personalWorkspace is not null)
        {
            await RefreshAuthoritativeStudentsAsync(cancellationToken);
        }
    }

    public async Task RefreshAuthoritativeStudentsAsync(CancellationToken cancellationToken = default)
    {
        if (_personalWorkspace is null)
        {
            return;
        }

        RecentObservationsStatusText = "正在重新验证当前教学权限…";
        RecentHistoryMoreText = string.Empty;
        RecentObservations.Clear();
        SelectedTeachingContexts.Clear();
        SelectedTeachingContext = null;

        var state = await _personalWorkspace.RefreshAsync(cancellationToken);
        if (state.Status != PersonalStudentWorkspaceStatus.Ready || state.Bootstrap is null)
        {
            _allStudents.Clear();
            _authoritativeStudents.Clear();
            ReplaceStudents(Array.Empty<StudentSummary>());
            SelectedStudent = null;
            RecentObservationsStatusText = WorkspaceFailureText(state.Status);
            return;
        }

        _authoritativeStudents.Clear();
        var summaries = new List<StudentSummary>();
        foreach (var student in _personalWorkspace.Students)
        {
            var id = WorkspaceStudentKey(student.OrganizationId, student.StudentId);
            _authoritativeStudents.Add(id, student);
            var subjects = string.Join(
                " / ",
                student.TeachingContexts
                    .Select(context => FormatSubjectKey(context.SubjectKey))
                    .Distinct(StringComparer.Ordinal));
            summaries.Add(new StudentSummary(
                id,
                student.DisplayName,
                string.Empty,
                subjects,
                -1));
        }

        _allStudents.Clear();
        _allStudents.AddRange(summaries);
        ReplaceStudents(_allStudents);
        SelectedStudent = Students.FirstOrDefault();
        RecentObservationsStatusText = Students.Count == 0
            ? "当前账号暂无有效任教学员。"
            : "选择学生查看服务器已确认的最近记录。";
    }

    public async Task LoadSelectedTeachingContextAsync(CancellationToken cancellationToken = default)
    {
        if (_personalWorkspace is null || SelectedTeachingContext is null)
        {
            return;
        }

        var selected = SelectedTeachingContext;
        RecentObservations.Clear();
        RecentHistoryMoreText = string.Empty;
        RecentObservationsStatusText = "正在读取最近记录…";

        var state = await _personalWorkspace.LoadRecentAsync(selected.Context, cancellationToken);
        if (!ReferenceEquals(selected, SelectedTeachingContext) && selected != SelectedTeachingContext)
        {
            return;
        }

        ApplyRecentState(state);
    }

    public void FilterStudents(string? query)
    {
        var selectedId = SelectedStudent?.Id;
        var filtered = StudentSearch.Filter(_allStudents, query);

        ReplaceStudents(filtered);

        if (selectedId is not null)
        {
            var retained = filtered.FirstOrDefault(student => student.Id == selectedId);
            if (retained is not null)
            {
                SelectedStudent = retained;
                return;
            }
        }

        // Filtering must not implicitly activate the first result. In compact
        // list/detail mode that would turn typing into an unexpected navigation
        // to Student Detail. The teacher explicitly chooses the next student.
        SelectedStudent = null;
    }

    private void PrepareTeachingContexts(StudentSummary? selectedStudent)
    {
        SelectedTeachingContexts.Clear();
        RecentObservations.Clear();
        RecentHistoryMoreText = string.Empty;

        if (selectedStudent is null || !_authoritativeStudents.TryGetValue(selectedStudent.Id, out var workspaceStudent))
        {
            SelectedTeachingContext = null;
            RecentObservationsStatusText = "选择学生查看服务器已确认的最近记录。";
            return;
        }

        foreach (var context in workspaceStudent.TeachingContexts)
        {
            SelectedTeachingContexts.Add(new TeachingContextOption(context, FormatSubjectKey(context.SubjectKey)));
        }

        if (SelectedTeachingContexts.Count == 1)
        {
            SelectedTeachingContext = SelectedTeachingContexts[0];
            RecentObservationsStatusText = "可读取最近记录。";
        }
        else
        {
            SelectedTeachingContext = null;
            RecentObservationsStatusText = "该学生有多个任教学科，请先选择学科。";
        }
    }

    private void ApplyRecentState(StudentRecentObservationsViewState state)
    {
        RecentObservations.Clear();
        RecentHistoryMoreText = string.Empty;

        switch (state.Status)
        {
            case StudentRecentObservationsViewStatus.Data when state.Snapshot is not null:
                foreach (var observation in state.Snapshot.Observations)
                {
                    RecentObservations.Add(observation);
                }
                RecentObservationsStatusText = "服务器已确认的最近记录";
                RecentHistoryMoreText = state.Snapshot.HasMore
                    ? "仅显示最近 20 条；更早历史尚未展开。"
                    : string.Empty;
                break;
            case StudentRecentObservationsViewStatus.Empty:
                RecentObservationsStatusText = "当前学科暂无已确认课堂观察。";
                break;
            case StudentRecentObservationsViewStatus.AuthenticationRequired:
                RecentObservationsStatusText = "登录状态已失效，未显示旧数据。";
                break;
            case StudentRecentObservationsViewStatus.AccessDenied:
                RecentObservationsStatusText = "当前教学权限已变化，未显示旧数据。";
                break;
            case StudentRecentObservationsViewStatus.TransientFailure:
                RecentObservationsStatusText = "暂时无法刷新最近记录，可稍后重试。";
                break;
            case StudentRecentObservationsViewStatus.ProtocolFailure:
                RecentObservationsStatusText = "服务器返回无法验证，已拒绝显示。";
                break;
            default:
                RecentObservationsStatusText = "教学上下文已更新，请重新选择。";
                break;
        }
    }

    private void ReplaceStudents(IEnumerable<StudentSummary> students)
    {
        Students.Clear();
        foreach (var student in students)
        {
            Students.Add(student);
        }
    }

    private static string WorkspaceStudentKey(Guid organizationId, Guid studentId) =>
        $"{organizationId:D}:{studentId:D}";

    private static string WorkspaceFailureText(PersonalStudentWorkspaceStatus status) => status switch
    {
        PersonalStudentWorkspaceStatus.AuthenticationRequired => "需要重新登录后读取任教学员。",
        PersonalStudentWorkspaceStatus.AccessDenied => "当前账号已无法访问个人教学工作区。",
        PersonalStudentWorkspaceStatus.TransientFailure => "暂时无法刷新任教学员，可稍后重试。",
        PersonalStudentWorkspaceStatus.ProtocolFailure => "教学上下文返回无法验证，已拒绝显示。",
        _ => "个人教学工作区尚未就绪。",
    };

    private static string FormatSubjectKey(string subjectKey) => subjectKey switch
    {
        "chinese" => "语文",
        "math" => "数学",
        "english" => "英语",
        "physics" => "物理",
        "chemistry" => "化学",
        "history" => "历史",
        "geography" => "地理",
        "biology" => "生物",
        "politics" => "道德与法治",
        _ => subjectKey,
    };
}

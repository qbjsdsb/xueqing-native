using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml;
using Xueqing.Windows.Core.Models;
using Xueqing.Windows.Core.Services;

namespace Xueqing.Windows.ViewModels;

public sealed record TeachingContextOption(
    PersonalTeachingContext Context,
    string DisplayName);

public sealed record LearningFocusDisplayItem(
    Guid CaseId,
    string Title,
    string StateLabel,
    string NextAction,
    string DueLabel);

public sealed record LearningCaseRecoveryLookup(
    bool IsAvailable,
    CreateLearningCaseRequest? Request);

public sealed record PendingLearningCaseRecoveryItem(
    CreateLearningCaseRequest Request,
    string StudentDisplayName,
    string SubjectDisplayName)
{
    public string Title => Request.Title;
    public string PrimaryActionText => Request.PrimaryActionText;
    public string DueLabel => Request.PrimaryActionDueOn is { } dueOn
        ? dueOn.ToString("MM月dd日")
        : "待安排";
}

public sealed class MainWindowViewModel : ObservableObject
{
    private readonly PersonalStudentWorkspaceCoordinator? _personalWorkspace;
    private readonly StudentLearningFocusCoordinator? _learningFocus;
    private readonly PersonalTodayActionsCoordinator? _today;
    private readonly ICreateLearningCaseCommand? _createLearningCase;
    private readonly ICreateLearningCaseRecoveryStore? _createLearningCaseRecovery;
    private readonly List<StudentSummary> _allStudents;
    private readonly Dictionary<string, PersonalStudentWorkspaceItem> _authoritativeStudents = new(StringComparer.Ordinal);
    private StudentSummary? _selectedStudent;
    private TeachingContextOption? _selectedTeachingContext;
    private string _recentObservationsStatusText = string.Empty;
    private string _recentHistoryMoreText = string.Empty;
    private string _currentFocusStatusText = string.Empty;
    private string _todayStatusText = string.Empty;
    private string _pendingLearningCaseRecoveryStatusText = string.Empty;
    private bool _initialized;

    public MainWindowViewModel()
        : this(null)
    {
    }

    public MainWindowViewModel(PersonalTeachingWorkspaceServices? teachingWorkspace)
    {
        _personalWorkspace = teachingWorkspace?.Students;
        _learningFocus = teachingWorkspace?.LearningFocus;
        _today = teachingWorkspace?.Today;
        _createLearningCase = teachingWorkspace?.CreateLearningCase;
        _createLearningCaseRecovery = teachingWorkspace?.CreateLearningCaseRecovery;
        _allStudents = _personalWorkspace is null
            ? SyntheticDataFactory.CreateStudents(1_000).ToList()
            : new List<StudentSummary>();

        Students = new ObservableCollection<StudentSummary>(_allStudents);
        SelectedTeachingContexts = new ObservableCollection<TeachingContextOption>();
        RecentObservations = new ObservableCollection<StudentRecentObservation>();
        CurrentFocus = new ObservableCollection<LearningFocusDisplayItem>();
        PendingLearningCaseRecoveries = new ObservableCollection<PendingLearningCaseRecoveryItem>();
        TodayActions = _personalWorkspace is null
            ? new ObservableCollection<TodayActionItem>(UxPrototypeFixtureFactory.CreateTodayActions())
            : new ObservableCollection<TodayActionItem>();
        OrganizationMembers = new ObservableCollection<OrganizationMemberRow>(UxPrototypeFixtureFactory.CreateOrganizationMembers());
        LearningCase = UxPrototypeFixtureFactory.CreateLearningCase();
        _selectedStudent = Students.FirstOrDefault();

        if (_personalWorkspace is null)
        {
            _recentObservationsStatusText = "UX 原型记录，仅用于布局与交互验证。";
            _currentFocusStatusText = "UX 原型关注项，仅用于布局与交互验证。";
            _todayStatusText = string.Empty;
        }
        else
        {
            _recentObservationsStatusText = "正在准备当前任教学员…";
            _currentFocusStatusText = "正在准备当前关注…";
            _todayStatusText = "正在准备今日行动…";
        }
    }

    public ObservableCollection<StudentSummary> Students { get; }

    public ObservableCollection<TodayActionItem> TodayActions { get; }

    public LearningCasePrototype LearningCase { get; }

    public ObservableCollection<OrganizationMemberRow> OrganizationMembers { get; }

    public ObservableCollection<TeachingContextOption> SelectedTeachingContexts { get; }

    public ObservableCollection<StudentRecentObservation> RecentObservations { get; }

    public ObservableCollection<LearningFocusDisplayItem> CurrentFocus { get; }

    public ObservableCollection<PendingLearningCaseRecoveryItem> PendingLearningCaseRecoveries { get; }

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
                CurrentFocus.Clear();
                RecentHistoryMoreText = string.Empty;
                RecentObservationsStatusText = value is null
                    ? (SelectedTeachingContexts.Count > 1 ? "选择学科后读取最近记录。" : "暂无可读取的教学上下文。")
                    : "可读取最近记录。";
                CurrentFocusStatusText = value is null
                    ? (SelectedTeachingContexts.Count > 1 ? "选择学科后读取当前关注。" : "暂无可读取的教学上下文。")
                    : "可读取当前关注。";
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

    public string CurrentFocusStatusText
    {
        get => _currentFocusStatusText;
        private set => SetProperty(ref _currentFocusStatusText, value);
    }

    public string TodayStatusText
    {
        get => _todayStatusText;
        private set => SetProperty(ref _todayStatusText, value);
    }

    public string PendingLearningCaseRecoveryStatusText
    {
        get => _pendingLearningCaseRecoveryStatusText;
        private set => SetProperty(ref _pendingLearningCaseRecoveryStatusText, value);
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
        CurrentFocusStatusText = "正在重新验证当前教学权限…";
        TodayStatusText = "正在重新验证今日行动权限…";
        RecentHistoryMoreText = string.Empty;
        RecentObservations.Clear();
        CurrentFocus.Clear();
        TodayActions.Clear();
        PendingLearningCaseRecoveries.Clear();
        PendingLearningCaseRecoveryStatusText = string.Empty;
        _learningFocus?.Reset();
        _today?.Reset();
        SelectedTeachingContexts.Clear();
        SelectedTeachingContext = null;

        var state = await _personalWorkspace.RefreshAsync(cancellationToken);
        if (state.Status != PersonalStudentWorkspaceStatus.Ready || state.Bootstrap is null)
        {
            _allStudents.Clear();
            _authoritativeStudents.Clear();
            ReplaceStudents(Array.Empty<StudentSummary>());
            SelectedStudent = null;
            var failureText = WorkspaceFailureText(state.Status);
            RecentObservationsStatusText = failureText;
            CurrentFocusStatusText = failureText;
            TodayStatusText = failureText;
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

        if (Students.Count == 0)
        {
            RecentObservationsStatusText = "当前账号暂无有效任教学员。";
            CurrentFocusStatusText = "当前账号暂无有效任教学员。";
        }

        await RefreshPendingLearningCaseRecoveriesAsync(state.Bootstrap, cancellationToken);
        await RefreshTodayAsync(state.Bootstrap.ActorAppUserId, cancellationToken);
    }

    public async Task LoadSelectedTeachingContextAsync(CancellationToken cancellationToken = default)
    {
        if (_personalWorkspace is null || SelectedTeachingContext is null)
        {
            return;
        }

        var bootstrap = _personalWorkspace.Current.Status == PersonalStudentWorkspaceStatus.Ready
            ? _personalWorkspace.Current.Bootstrap
            : null;
        if (bootstrap is null)
        {
            return;
        }

        var selected = SelectedTeachingContext;
        RecentObservations.Clear();
        CurrentFocus.Clear();
        RecentHistoryMoreText = string.Empty;
        RecentObservationsStatusText = "正在读取最近记录…";
        CurrentFocusStatusText = "正在读取当前关注…";

        var recentTask = _personalWorkspace.LoadRecentAsync(selected.Context, cancellationToken);
        Task<StudentLearningFocusViewState>? focusTask = null;
        if (_learningFocus is not null)
        {
            focusTask = _learningFocus.LoadAsync(
                ToLearningScope(selected.Context),
                bootstrap.ActorAppUserId,
                cancellationToken);
        }

        var recentState = await recentTask;
        StudentLearningFocusViewState? focusState = focusTask is null
            ? null
            : await focusTask;

        if (!ReferenceEquals(selected, SelectedTeachingContext) && selected != SelectedTeachingContext)
        {
            return;
        }

        ApplyRecentState(recentState);
        if (focusState is not null)
        {
            ApplyFocusState(focusState);
        }
    }

    public async Task<LearningCaseRecoveryLookup> FindPendingLearningCaseForObservationAsync(
        StudentRecentObservation observation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(observation);

        if (_personalWorkspace is null ||
            _createLearningCaseRecovery is null ||
            SelectedTeachingContext is null)
        {
            return new LearningCaseRecoveryLookup(false, null);
        }

        var selected = SelectedTeachingContext;
        var workspaceState = _personalWorkspace.Current;
        var bootstrap = workspaceState.Status == PersonalStudentWorkspaceStatus.Ready
            ? workspaceState.Bootstrap
            : null;
        if (bootstrap is null ||
            !ContainsExactContext(bootstrap, selected.Context) ||
            !RecentSnapshotContains(selected.Context, observation.ObservationId))
        {
            return new LearningCaseRecoveryLookup(false, null);
        }

        try
        {
            var pending = await _createLearningCaseRecovery.FindBySourceObservationAsync(
                bootstrap.ActorAppUserId,
                selected.Context.OrganizationId,
                selected.Context.StudentId,
                selected.Context.SubjectProfileId,
                observation.ObservationId,
                cancellationToken);
            return new LearningCaseRecoveryLookup(true, pending);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return new LearningCaseRecoveryLookup(false, null);
        }
    }

    public async Task<CreateLearningCaseResult> CreateLearningCaseFromObservationAsync(
        StudentRecentObservation observation,
        string title,
        string primaryActionText,
        DateOnly? dueOn,
        Guid operationId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(observation);

        if (_personalWorkspace is null ||
            _learningFocus is null ||
            _today is null ||
            _createLearningCase is null ||
            _createLearningCaseRecovery is null ||
            SelectedTeachingContext is null)
        {
            return CreateLearningCaseResult.Failed(
                CreateLearningCaseFailureKind.AuthorityChanged,
                "XQ_CLIENT_TEACHING_CONTEXT_UNAVAILABLE");
        }

        var selected = SelectedTeachingContext;
        var workspaceState = _personalWorkspace.Current;
        var bootstrap = workspaceState.Status == PersonalStudentWorkspaceStatus.Ready
            ? workspaceState.Bootstrap
            : null;
        if (bootstrap is null ||
            !ContainsExactContext(bootstrap, selected.Context) ||
            !RecentSnapshotContains(selected.Context, observation.ObservationId))
        {
            return CreateLearningCaseResult.Failed(
                CreateLearningCaseFailureKind.AuthorityChanged,
                "XQ_CLIENT_TEACHING_CONTEXT_STALE");
        }

        var request = new CreateLearningCaseRequest(
            operationId,
            selected.Context.OrganizationId,
            selected.Context.StudentId,
            selected.Context.SubjectProfileId,
            selected.Context.AssignmentId,
            title,
            primaryActionText,
            dueOn,
            observation.ObservationId);

        try
        {
            await _createLearningCaseRecovery.SaveAsync(
                bootstrap.ActorAppUserId,
                request,
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return CreateLearningCaseResult.Failed(
                CreateLearningCaseFailureKind.LocalDurabilityFailure,
                "XQ_LOCAL_COMMAND_RECOVERY_UNAVAILABLE");
        }

        var result = await _createLearningCase.ExecuteAsync(
            request,
            bootstrap.ActorAppUserId,
            cancellationToken);

        result = await FinalizeLearningCaseRecoveryAsync(
            bootstrap.ActorAppUserId,
            request,
            result,
            cancellationToken);

        if (result.IsSuccess)
        {
            await RefreshLearningAfterCaseAsync(
                selected,
                bootstrap.ActorAppUserId,
                cancellationToken);
            return result;
        }

        if (result.Failure?.Kind is
            CreateLearningCaseFailureKind.AuthenticationRequired or
            CreateLearningCaseFailureKind.AuthorityChanged)
        {
            await RefreshAuthoritativeStudentsAsync(cancellationToken);
        }

        return result;
    }

    public async Task<CreateLearningCaseResult> RetryPendingLearningCaseAsync(
        CreateLearningCaseRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (_personalWorkspace is null ||
            _createLearningCase is null ||
            _createLearningCaseRecovery is null)
        {
            return CreateLearningCaseResult.Failed(
                CreateLearningCaseFailureKind.AuthorityChanged,
                "XQ_CLIENT_TEACHING_CONTEXT_UNAVAILABLE");
        }

        var workspaceState = _personalWorkspace.Current;
        var bootstrap = workspaceState.Status == PersonalStudentWorkspaceStatus.Ready
            ? workspaceState.Bootstrap
            : null;
        if (bootstrap is null)
        {
            return CreateLearningCaseResult.Failed(
                CreateLearningCaseFailureKind.AuthenticationRequired,
                "XQ_CLIENT_BOOTSTRAP_UNAVAILABLE");
        }

        var result = await _createLearningCase.ExecuteAsync(
            request,
            bootstrap.ActorAppUserId,
            cancellationToken);
        result = await FinalizeLearningCaseRecoveryAsync(
            bootstrap.ActorAppUserId,
            request,
            result,
            cancellationToken);

        if (result.IsSuccess ||
            result.Failure?.Kind is
                CreateLearningCaseFailureKind.AuthenticationRequired or
                CreateLearningCaseFailureKind.AuthorityChanged)
        {
            await RefreshAuthoritativeStudentsAsync(cancellationToken);
        }
        else if (result.Failure?.Kind is not
                     (CreateLearningCaseFailureKind.ResultUnknown or
                      CreateLearningCaseFailureKind.Transient))
        {
            await RefreshPendingLearningCaseRecoveriesAsync(bootstrap, cancellationToken);
        }

        return result;
    }

    private async Task RefreshPendingLearningCaseRecoveriesAsync(
        PersonalBootstrapSnapshot bootstrap,
        CancellationToken cancellationToken)
    {
        PendingLearningCaseRecoveries.Clear();
        PendingLearningCaseRecoveryStatusText = string.Empty;

        if (_createLearningCaseRecovery is null || _personalWorkspace is null)
        {
            return;
        }

        try
        {
            foreach (var organizationId in bootstrap.TeachingContexts
                         .Select(context => context.OrganizationId)
                         .Distinct())
            {
                var pending = await _createLearningCaseRecovery.ListPendingAsync(
                    bootstrap.ActorAppUserId,
                    organizationId,
                    cancellationToken);

                foreach (var request in pending)
                {
                    var context = bootstrap.TeachingContexts.FirstOrDefault(candidate =>
                        candidate.OrganizationId == request.OrganizationId &&
                        candidate.StudentId == request.StudentId &&
                        candidate.SubjectProfileId == request.SubjectProfileId &&
                        candidate.AssignmentId == request.OwnerAssignmentId);
                    var student = _personalWorkspace.Students.FirstOrDefault(candidate =>
                        candidate.OrganizationId == request.OrganizationId &&
                        candidate.StudentId == request.StudentId);

                    PendingLearningCaseRecoveries.Add(
                        new PendingLearningCaseRecoveryItem(
                            request,
                            student?.DisplayName ?? "当前学生",
                            context is null
                                ? "教学上下文已变化"
                                : FormatSubjectKey(context.SubjectKey)));
                }
            }

            if (PendingLearningCaseRecoveries.Count > 0)
            {
                PendingLearningCaseRecoveryStatusText =
                    $"有 {PendingLearningCaseRecoveries.Count} 条提交结果尚未确认，请继续原操作确认结果。";
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            PendingLearningCaseRecoveries.Clear();
            PendingLearningCaseRecoveryStatusText =
                "本机待确认提交暂时无法读取。为避免重复创建，请先不要重新发起相同学情问题。";
        }
    }

    private async Task<CreateLearningCaseResult> FinalizeLearningCaseRecoveryAsync(
        Guid actorAppUserId,
        CreateLearningCaseRequest request,
        CreateLearningCaseResult result,
        CancellationToken cancellationToken)
    {
        if (_createLearningCaseRecovery is null)
        {
            return result;
        }

        var mustKeepRecovery = result.Failure?.Kind is
            CreateLearningCaseFailureKind.ResultUnknown or
            CreateLearningCaseFailureKind.Transient;
        if (mustKeepRecovery)
        {
            return result;
        }

        if (result.IsSuccess)
        {
            try
            {
                await _createLearningCaseRecovery.RemoveAsync(
                    actorAppUserId,
                    request.OrganizationId,
                    request.OperationId,
                    cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                // A committed result remains authoritative. Leaving the exact
                // operation pending is safe: the next explicit retry resolves
                // the same operation_id and cannot duplicate the Case.
            }

            return result;
        }

        try
        {
            await _createLearningCaseRecovery.MarkRejectedAsync(
                actorAppUserId,
                request.OrganizationId,
                request.OperationId,
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return CreateLearningCaseResult.Failed(
                CreateLearningCaseFailureKind.LocalDurabilityFailure,
                "XQ_LOCAL_COMMAND_REJECTION_QUARANTINE_FAILED");
        }

        try
        {
            await _createLearningCaseRecovery.RemoveAsync(
                actorAppUserId,
                request.OrganizationId,
                request.OperationId,
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            // The row is already quarantined from pending discovery. A later
            // corrected Save for the same source removes rejected_cleanup
            // rows transactionally before inserting the new intent.
        }

        return result;
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

    private async Task RefreshLearningAfterCaseAsync(
        TeachingContextOption selected,
        Guid actorAppUserId,
        CancellationToken cancellationToken)
    {
        if (_learningFocus is null)
        {
            return;
        }

        var focusTask = _learningFocus.LoadAsync(
            ToLearningScope(selected.Context),
            actorAppUserId,
            cancellationToken);
        var todayTask = RefreshTodayAsync(actorAppUserId, cancellationToken);

        var focusState = await focusTask;
        await todayTask;

        if (ReferenceEquals(selected, SelectedTeachingContext) || selected == SelectedTeachingContext)
        {
            ApplyFocusState(focusState);
        }
    }

    private async Task RefreshTodayAsync(
        Guid actorAppUserId,
        CancellationToken cancellationToken)
    {
        if (_today is null)
        {
            return;
        }

        TodayStatusText = "正在读取今日行动…";
        TodayActions.Clear();

        var state = await _today.LoadAsync(actorAppUserId, cancellationToken);
        ApplyTodayState(state);
    }

    private void PrepareTeachingContexts(StudentSummary? selectedStudent)
    {
        SelectedTeachingContexts.Clear();
        RecentObservations.Clear();
        CurrentFocus.Clear();
        RecentHistoryMoreText = string.Empty;

        if (selectedStudent is null || !_authoritativeStudents.TryGetValue(selectedStudent.Id, out var workspaceStudent))
        {
            SelectedTeachingContext = null;
            RecentObservationsStatusText = "选择学生查看服务器已确认的最近记录。";
            CurrentFocusStatusText = "选择学生查看当前关注。";
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
            CurrentFocusStatusText = "可读取当前关注。";
        }
        else
        {
            SelectedTeachingContext = null;
            RecentObservationsStatusText = "该学生有多个任教学科，请先选择学科。";
            CurrentFocusStatusText = "该学生有多个任教学科，请先选择学科。";
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

    private void ApplyFocusState(StudentLearningFocusViewState state)
    {
        CurrentFocus.Clear();

        switch (state.Status)
        {
            case StudentLearningFocusViewStatus.Data when state.Snapshot is not null:
                foreach (var learningCase in state.Snapshot.Cases)
                {
                    CurrentFocus.Add(ToFocusDisplayItem(learningCase));
                }
                CurrentFocusStatusText = state.Snapshot.HasMore
                    ? "显示最近更新的 3 个关注项；更早内容可在后续历史视图中查看。"
                    : "当前关注";
                break;
            case StudentLearningFocusViewStatus.Empty:
                CurrentFocusStatusText = "当前学科暂无持续跟进的问题。";
                break;
            case StudentLearningFocusViewStatus.AuthenticationRequired:
                CurrentFocusStatusText = "登录状态已失效，未显示旧关注项。";
                break;
            case StudentLearningFocusViewStatus.AccessDenied:
                CurrentFocusStatusText = "当前教学权限已变化，未显示旧关注项。";
                break;
            case StudentLearningFocusViewStatus.ServerInvariant:
                CurrentFocusStatusText = "当前学情数据需要服务器校验，已停止展示。";
                break;
            case StudentLearningFocusViewStatus.TransientFailure:
                CurrentFocusStatusText = "暂时无法刷新当前关注，可稍后重试。";
                break;
            case StudentLearningFocusViewStatus.ProtocolFailure:
                CurrentFocusStatusText = "服务器返回无法验证，已拒绝显示关注项。";
                break;
            default:
                CurrentFocusStatusText = "教学上下文已更新，请重新选择。";
                break;
        }
    }

    private void ApplyTodayState(PersonalTodayActionsViewState state)
    {
        TodayActions.Clear();

        switch (state.Status)
        {
            case PersonalTodayActionsViewStatus.Data when state.Snapshot is not null:
                foreach (var action in state.Snapshot.Actions)
                {
                    TodayActions.Add(ToTodayActionItem(action));
                }
                TodayStatusText = state.Snapshot.HasMore
                    ? "仅显示前 200 条待办行动。"
                    : string.Empty;
                break;
            case PersonalTodayActionsViewStatus.Empty:
                TodayStatusText = "当前没有待办教学行动。";
                break;
            case PersonalTodayActionsViewStatus.AuthenticationRequired:
                TodayStatusText = "登录状态已失效，未显示旧行动。";
                break;
            case PersonalTodayActionsViewStatus.AccessDenied:
                TodayStatusText = "当前教学权限已变化，未显示旧行动。";
                break;
            case PersonalTodayActionsViewStatus.ServerInvariant:
                TodayStatusText = "行动数据需要服务器校验，已停止展示。";
                break;
            case PersonalTodayActionsViewStatus.TransientFailure:
                TodayStatusText = "暂时无法刷新今日行动，可稍后重试。";
                break;
            case PersonalTodayActionsViewStatus.ProtocolFailure:
                TodayStatusText = "服务器返回无法验证，已拒绝显示行动。";
                break;
            default:
                TodayStatusText = "今日行动尚未就绪。";
                break;
        }
    }

    private bool RecentSnapshotContains(
        PersonalTeachingContext context,
        Guid observationId)
    {
        var snapshot = _personalWorkspace?.RecentObservations.Snapshot;
        return snapshot is not null &&
               snapshot.OrganizationId == context.OrganizationId &&
               snapshot.StudentId == context.StudentId &&
               snapshot.SubjectProfileId == context.SubjectProfileId &&
               snapshot.AssignmentId == context.AssignmentId &&
               snapshot.Observations.Any(observation => observation.ObservationId == observationId);
    }

    private static bool ContainsExactContext(
        PersonalBootstrapSnapshot bootstrap,
        PersonalTeachingContext candidate) =>
        bootstrap.TeachingContexts.Any(context =>
            context.OrganizationId == candidate.OrganizationId &&
            context.StudentId == candidate.StudentId &&
            context.SubjectProfileId == candidate.SubjectProfileId &&
            context.AssignmentId == candidate.AssignmentId);

    private void ReplaceStudents(IEnumerable<StudentSummary> students)
    {
        Students.Clear();
        foreach (var student in students)
        {
            Students.Add(student);
        }
    }

    private static StudentLearningScope ToLearningScope(PersonalTeachingContext context) =>
        new(context.OrganizationId, context.StudentId, context.SubjectProfileId);

    private static LearningFocusDisplayItem ToFocusDisplayItem(StudentLearningCaseFocus learningCase) =>
        new(
            learningCase.CaseId,
            learningCase.Title,
            learningCase.State switch
            {
                LearningCaseState.New => "新建",
                LearningCaseState.Confirmed => "已确认",
                LearningCaseState.Intervening => "跟进中",
                LearningCaseState.PendingVerification => "待验证",
                LearningCaseState.Stable => "稳定",
                _ => string.Empty,
            },
            learningCase.PrimaryAction.ActionText,
            learningCase.PrimaryAction.DueBucket switch
            {
                ActionDueBucket.Overdue => learningCase.PrimaryAction.DueOn is null
                    ? "逾期"
                    : $"逾期 · {learningCase.PrimaryAction.DueOn.Value:MM月dd日}",
                ActionDueBucket.Today => "今天",
                ActionDueBucket.Undated => "待安排",
                ActionDueBucket.Future => learningCase.PrimaryAction.DueOn is null
                    ? "之后"
                    : learningCase.PrimaryAction.DueOn.Value.ToString("MM月dd日"),
                _ => string.Empty,
            });

    private static TodayActionItem ToTodayActionItem(PersonalTodayAction action) =>
        new(
            action.ActionId.ToString("D"),
            action.StudentId.ToString("D"),
            action.StudentDisplayName,
            FormatSubjectKey(action.SubjectKey),
            action.ActionText,
            action.DueBucket switch
            {
                ActionDueBucket.Overdue => TodayActionBucket.Overdue,
                ActionDueBucket.Today => TodayActionBucket.Today,
                ActionDueBucket.Undated => TodayActionBucket.Undated,
                ActionDueBucket.Future => TodayActionBucket.Future,
                _ => TodayActionBucket.Undated,
            },
            action.DueOn,
            PrototypeInteractionState.Committed);

    private static string WorkspaceStudentKey(Guid organizationId, Guid studentId) =>
        $"{organizationId:D}:{studentId:D}";

    private static string WorkspaceFailureText(PersonalStudentWorkspaceStatus status) => status switch
    {
        PersonalStudentWorkspaceStatus.AuthenticationRequired => "需要重新登录后读取个人教学工作区。",
        PersonalStudentWorkspaceStatus.AccessDenied => "当前账号已无法访问个人教学工作区。",
        PersonalStudentWorkspaceStatus.TransientFailure => "暂时无法刷新个人教学工作区，可稍后重试。",
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

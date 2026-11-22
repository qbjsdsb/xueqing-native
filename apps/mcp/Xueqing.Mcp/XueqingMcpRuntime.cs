using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Xueqing.Linux.Infrastructure.LocalData;
using Xueqing.Linux.Infrastructure.Security;
using Xueqing.Windows.Core.Models;
using Xueqing.Windows.Core.Services;
using Xueqing.Windows.Infrastructure.Auth;
using Xueqing.Windows.Infrastructure.Deployment;
using Xueqing.Windows.Infrastructure.LocalData;
using Xueqing.Windows.Infrastructure.Remote;
using Xueqing.Windows.Infrastructure.Sync;
using Xueqing.Windows.ViewModels;

namespace Xueqing.Mcp;

public interface IXueqingMcpBackend : IDisposable
{
    Task<object> GetStatusAsync(CancellationToken cancellationToken);
    Task<object> ListStudentsAsync(string? query, int page, int pageSize, CancellationToken cancellationToken);
    Task<object> GetTodayActionsAsync(string? bucket, int limit, CancellationToken cancellationToken);
    Task<object> GetStudentContextAsync(string studentId, CancellationToken cancellationToken);
    Task<object> GetStudentCasesAsync(string studentId, string? subjectProfileId, int timelineLimit, CancellationToken cancellationToken);
    Task<object> GetObservationDraftAsync(string studentId, string? organizationId, string? subjectProfileId, string? assignmentId, CancellationToken cancellationToken);
    Task<object> SaveObservationDraftAsync(string studentId, long expectedEpoch, string text, string? organizationId, string? subjectProfileId, string? assignmentId, CancellationToken cancellationToken);
    Task<object> ListStudentPapersAsync(string studentId, string? subjectProfileId, int windowDays, int limit, CancellationToken cancellationToken);
    Task<object> GetPaperDetailAsync(string studentId, string paperSubmissionId, string? subjectProfileId, CancellationToken cancellationToken);
    Task<object> GetStudentLongitudinalMetricsAsync(string studentId, string? subjectProfileId, int windowDays, CancellationToken cancellationToken);
    Task<object> SaveStudyPlanDraftAsync(string studentId, string title, string briefMarkdown, string? evidenceRefsJson, string? proposedActionsJson, int windowDays, int paperCountAnalyzed, string? organizationId, string? subjectProfileId, string? assignmentId, CancellationToken cancellationToken);
}

internal static class XueqingMcpHostHelpers
{
    public static string PlatformName =>
        OperatingSystem.IsLinux() ? "linux" :
        OperatingSystem.IsWindows() ? "windows" :
        "unsupported";

    public static string ResolveDataRoot(string? explicitRoot)
    {
        if (!string.IsNullOrWhiteSpace(explicitRoot))
        {
            Directory.CreateDirectory(explicitRoot);
            return explicitRoot;
        }

        if (OperatingSystem.IsLinux())
        {
            return LinuxLocalStatePaths.GetAppDataRoot();
        }

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var root = Path.Combine(localAppData, "Xueqing");
        Directory.CreateDirectory(root);
        return root;
    }

    public static ILocalDatabaseKeyStore CreatePlatformKeyStore(string databasePath)
    {
        if (OperatingSystem.IsLinux())
        {
            return new LinuxSecretServiceDatabaseKeyStore(databasePath);
        }

        if (OperatingSystem.IsWindows())
        {
            return new WindowsDpapiDatabaseKeyStore(databasePath);
        }

        throw new PlatformNotSupportedException(
            "Encrypted local database storage is currently supported on Linux (libsecret) and Windows (DPAPI).");
    }

    public static IRefreshTokenVault CreatePlatformRefreshTokenVault(
        string dataRoot,
        string environmentId,
        string trustDomainId)
    {
        if (OperatingSystem.IsLinux())
        {
            return new LinuxSecretServiceRefreshTokenVault(environmentId, trustDomainId);
        }

        if (OperatingSystem.IsWindows())
        {
            return new WindowsDpapiRefreshTokenVault(
                Path.Combine(dataRoot, "auth"),
                environmentId,
                trustDomainId);
        }

        throw new PlatformNotSupportedException(
            "Encrypted refresh token vault is currently supported on Linux (libsecret) and Windows (DPAPI).");
    }

    public static Guid DeterministicGuid(string seed)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes("xueqing-mcp-v1/" + seed));
        var bytes = new byte[16];
        Array.Copy(hash, 0, bytes, 0, 16);
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x50);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
        return new Guid(bytes);
    }
}

/// <summary>
/// Prototype MCP backend backed by the unmodified MainWindowViewModel synthetic dataset
/// plus a real encrypted SQLite3MC + OS keyring observation draft database.
/// </summary>
internal sealed class PrototypeXueqingMcpBackend : IXueqingMcpBackend
{
    private static readonly Guid PrototypeOrgId = XueqingMcpHostHelpers.DeterministicGuid("prototype-org");
    private static readonly Guid PrototypeSubjectProfileId = XueqingMcpHostHelpers.DeterministicGuid("prototype-subject-profile");
    private static readonly Guid PrototypeAssignmentId = XueqingMcpHostHelpers.DeterministicGuid("prototype-assignment");

    private readonly MainWindowViewModel _viewModel = new();
    private readonly string _draftDbPath;
    private readonly SqliteObservationDraftStore _draftStore;
    private readonly SqliteObservationDraftStore _studyPlanDraftStore;

    public PrototypeXueqingMcpBackend(string? explicitDataRoot)
    {
        var dataRoot = XueqingMcpHostHelpers.ResolveDataRoot(explicitDataRoot);
        _draftDbPath = Path.Combine(dataRoot, "mcp-prototype", "observation-draft.db");
        var keyStore = XueqingMcpHostHelpers.CreatePlatformKeyStore(_draftDbPath);
        _draftStore = new SqliteObservationDraftStore(_draftDbPath, keyStore);

        var planDbPath = Path.Combine(dataRoot, "mcp-prototype", "study-plan-draft.db");
        _studyPlanDraftStore = new SqliteObservationDraftStore(
            planDbPath,
            XueqingMcpHostHelpers.CreatePlatformKeyStore(planDbPath));
    }

    public Task<object> GetStatusAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<object>(new
        {
            contract = "xueqing_mcp_v1",
            mode = "prototype",
            platform = XueqingMcpHostHelpers.PlatformName,
            architecture = RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant(),
            sessionState = "prototype_synthetic",
            writePolicy = "read_local_observation_draft_and_server_study_plan_draft_only",
            totalStudents = _viewModel.Students.Count,
            todayActionCount = _viewModel.TodayActions.Count,
            encryptedDraftStoreReady = true,
            note = "Running with deterministic fictional data and real SQLite3MC + OS keyring encrypted draft storage.",
        });
    }

    public Task<object> ListStudentsAsync(string? query, int page, int pageSize, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var clampedPage = Math.Max(1, page);
        var clampedSize = Math.Clamp(pageSize, 1, 50);

        lock (_viewModel)
        {
            _viewModel.FilterStudents(query);
            var total = _viewModel.Students.Count;
            var items = _viewModel.Students
                .Skip((clampedPage - 1) * clampedSize)
                .Take(clampedSize)
                .Select(s => new
                {
                    studentId = ResolvePrototypeStudentUuid(s.Id).ToString("D"),
                    prototypeKey = s.Id,
                    displayName = s.DisplayName,
                    studentCode = s.StudentCode,
                    primarySubject = s.PrimarySubject,
                    activeCaseCount = s.ActiveCaseCount,
                    deepLink = $"xueqing://student/{ResolvePrototypeStudentUuid(s.Id):D}",
                })
                .ToArray();
            _viewModel.FilterStudents(string.Empty);

            return Task.FromResult<object>(new
            {
                mode = "prototype",
                query = query ?? string.Empty,
                page = clampedPage,
                pageSize = clampedSize,
                totalMatches = total,
                hasMore = clampedPage * clampedSize < total,
                students = items,
            });
        }
    }

    public Task<object> GetTodayActionsAsync(string? bucket, int limit, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var clampedLimit = Math.Clamp(limit, 1, 50);
        var normalizedBucket = bucket?.Trim().ToLowerInvariant();

        var filtered = _viewModel.TodayActions
            .Where(a => string.IsNullOrEmpty(normalizedBucket) ||
                        a.Bucket.ToString().Equals(normalizedBucket, StringComparison.OrdinalIgnoreCase))
            .Take(clampedLimit)
            .Select(a => new
            {
                actionId = a.Id,
                studentId = ResolvePrototypeStudentUuid(a.StudentId).ToString("D"),
                studentDisplayName = a.StudentDisplayName,
                subject = a.Subject,
                title = a.Title,
                bucket = a.Bucket.ToString().ToLowerInvariant(),
                bucketLabel = a.BucketLabel,
                dueDate = a.DueDate?.ToString("yyyy-MM-dd"),
                dueLabel = a.DueLabel,
                interactionState = a.InteractionState.ToString(),
                interactionStateLabel = a.InteractionStateLabel,
            })
            .ToArray();

        return Task.FromResult<object>(new
        {
            mode = "prototype",
            bucket = normalizedBucket ?? "all",
            count = filtered.Length,
            actions = filtered,
            deepLink = "xueqing://today",
        });
    }

    public Task<object> GetStudentContextAsync(string studentId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var student = FindPrototypeStudent(studentId);
        if (student is null)
        {
            return Task.FromResult<object>(new
            {
                found = false,
                error = "XQ_STUDENT_NOT_FOUND",
                message = "No matching student in prototype dataset.",
            });
        }

        var studentUuid = ResolvePrototypeStudentUuid(student.Id);
        var relatedActions = _viewModel.TodayActions
            .Where(a => string.Equals(a.StudentId, student.Id, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(a.StudentDisplayName, student.DisplayName, StringComparison.Ordinal))
            .Select(a => new
            {
                a.Title,
                bucket = a.BucketLabel,
                due = a.DueLabel,
                state = a.InteractionStateLabel,
            })
            .ToArray();

        return Task.FromResult<object>(new
        {
            found = true,
            mode = "prototype",
            student = new
            {
                studentId = studentUuid.ToString("D"),
                prototypeKey = student.Id,
                student.DisplayName,
                student.StudentCode,
                student.PrimarySubject,
                student.ActiveCaseCount,
            },
            draftScope = new
            {
                organizationId = PrototypeOrgId.ToString("D"),
                studentId = studentUuid.ToString("D"),
                subjectProfileId = PrototypeSubjectProfileId.ToString("D"),
                assignmentId = PrototypeAssignmentId.ToString("D"),
            },
            todayActions = relatedActions,
            deepLink = $"xueqing://student/{studentUuid:D}",
        });
    }

    public Task<object> GetStudentCasesAsync(
        string studentId,
        string? subjectProfileId,
        int timelineLimit,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var clampedTimeline = Math.Clamp(timelineLimit, 1, 50);
        var lc = _viewModel.LearningCase;
        var caseUuid = XueqingMcpHostHelpers.DeterministicGuid("prototype-case/" + lc.Id);

        return Task.FromResult<object>(new
        {
            mode = "prototype",
            requestedStudentId = studentId,
            cases = new[]
            {
                new
                {
                    caseId = caseUuid.ToString("D"),
                    lc.Title,
                    lc.StudentDisplayName,
                    lc.Subject,
                    lc.StateLabel,
                    lc.ResponsibleTeacher,
                    lc.NextAction,
                    totalTimelineEntries = lc.Timeline.Count,
                    timeline = lc.Timeline.Take(clampedTimeline).Select(e => new
                    {
                        e.Id,
                        occurredAt = e.OccurredAt,
                        kind = e.Kind.ToString().ToLowerInvariant(),
                        e.Heading,
                        e.Body,
                        e.ActorDisplayName,
                    }).ToArray(),
                    deepLink = $"xueqing://learning/{caseUuid:D}",
                },
            },
        });
    }

    public async Task<object> GetObservationDraftAsync(
        string studentId,
        string? organizationId,
        string? subjectProfileId,
        string? assignmentId,
        CancellationToken cancellationToken)
    {
        var scope = ResolveScope(studentId, organizationId, subjectProfileId, assignmentId);
        var opened = await _draftStore.OpenAsync(scope, cancellationToken);

        return new
        {
            mode = "prototype",
            encrypted = true,
            scope = new
            {
                organizationId = scope.OrganizationId.ToString("D"),
                studentId = scope.StudentId.ToString("D"),
                subjectProfileId = scope.SubjectProfileId.ToString("D"),
                assignmentId = scope.AssignmentId.ToString("D"),
            },
            epoch = opened.Epoch,
            hasRecoveredDraft = opened.Recovered is not null,
            text = opened.Recovered?.Text ?? string.Empty,
            updatedAt = opened.Recovered?.UpdatedAt,
            deepLink = $"xueqing://student/{scope.StudentId:D}",
        };
    }

    public async Task<object> SaveObservationDraftAsync(
        string studentId,
        long expectedEpoch,
        string text,
        string? organizationId,
        string? subjectProfileId,
        string? assignmentId,
        CancellationToken cancellationToken)
    {
        var scope = ResolveScope(studentId, organizationId, subjectProfileId, assignmentId);
        var saved = await _draftStore.SaveAsync(
            scope,
            expectedEpoch,
            text ?? string.Empty,
            DateTimeOffset.UtcNow,
            cancellationToken);

        return new
        {
            mode = "prototype",
            encrypted = true,
            saved,
            conflict = !saved,
            submittedToServer = false,
            expectedEpoch,
            deepLink = $"xueqing://student/{scope.StudentId:D}",
            nextStep = saved
                ? "Draft saved locally in encrypted storage. Open the native Xueqing app via deepLink for teacher review and explicit submission."
                : "Epoch mismatch: re-read the draft via xueqing_get_observation_draft before retrying to avoid overwriting newer teacher edits.",
        };
    }

    private StudentSummary? FindPrototypeStudent(string studentId)
    {
        if (string.IsNullOrWhiteSpace(studentId))
        {
            return null;
        }

        var trimmed = studentId.Trim();
        lock (_viewModel)
        {
            _viewModel.FilterStudents(string.Empty);
            return _viewModel.Students.FirstOrDefault(s =>
                string.Equals(s.Id, trimmed, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(ResolvePrototypeStudentUuid(s.Id).ToString("D"), trimmed, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(s.StudentCode, trimmed, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(s.DisplayName, trimmed, StringComparison.Ordinal));
        }
    }

    private ObservationDraftScope ResolveScope(
        string studentId,
        string? organizationId,
        string? subjectProfileId,
        string? assignmentId)
    {
        var resolvedStudent = Guid.TryParse(studentId, out var parsedStudent) && parsedStudent != Guid.Empty
            ? parsedStudent
            : ResolvePrototypeStudentUuid(FindPrototypeStudent(studentId)?.Id ?? studentId);
        var orgId = Guid.TryParse(organizationId, out var parsedOrg) && parsedOrg != Guid.Empty
            ? parsedOrg
            : PrototypeOrgId;
        var subjId = Guid.TryParse(subjectProfileId, out var parsedSubj) && parsedSubj != Guid.Empty
            ? parsedSubj
            : PrototypeSubjectProfileId;
        var assignId = Guid.TryParse(assignmentId, out var parsedAssign) && parsedAssign != Guid.Empty
            ? parsedAssign
            : PrototypeAssignmentId;

        return new ObservationDraftScope(orgId, resolvedStudent, subjId, assignId);
    }

    private static Guid ResolvePrototypeStudentUuid(string id) =>
        Guid.TryParse(id, out var parsed) && parsed != Guid.Empty
            ? parsed
            : XueqingMcpHostHelpers.DeterministicGuid("prototype-student/" + id);

    public Task<object> ListStudentPapersAsync(
        string studentId,
        string? subjectProfileId,
        int windowDays,
        int limit,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var student = FindPrototypeStudent(studentId);
        if (student is null)
        {
            return Task.FromResult<object>(new { found = false, error = "XQ_STUDENT_NOT_FOUND" });
        }

        var snapshot = BuildPrototypeLongitudinal(student);
        var clampedLimit = Math.Clamp(limit, 1, 50);
        var papers = snapshot.Papers.Take(clampedLimit).Select(p => new
        {
            paperSubmissionId = p.PaperSubmissionId.ToString("D"),
            paperDate = p.PaperDate.ToString("yyyy-MM-dd"),
            paperKind = PaperEvidenceWireNames.ToWire(p.PaperKind),
            kindLabel = p.KindLabel,
            p.Title,
            gradingSource = p.GradingSource.ToString(),
            confirmationState = p.ConfirmationState.ToString(),
            confirmationLabel = p.ConfirmationLabel,
            p.QuestionCount,
            p.MistakeCount,
            p.ScoreEarned,
            p.ScoreTotal,
            scoreDisplay = p.ScoreDisplay,
            p.DurationMinutes,
            pageCount = p.Pages.Count,
            p.ExtractionSummary,
        }).ToArray();

        return Task.FromResult<object>(new
        {
            found = true,
            mode = "prototype",
            studentId = snapshot.StudentId.ToString("D"),
            studentDisplayName = snapshot.StudentDisplayName,
            subjectKey = snapshot.SubjectKey,
            windowDays = Math.Clamp(windowDays, 7, 365),
            count = papers.Length,
            papers,
            deepLink = $"xueqing://student/{snapshot.StudentId:D}",
        });
    }

    public Task<object> GetPaperDetailAsync(
        string studentId,
        string paperSubmissionId,
        string? subjectProfileId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var student = FindPrototypeStudent(studentId);
        if (student is null)
        {
            return Task.FromResult<object>(new { found = false, error = "XQ_STUDENT_NOT_FOUND" });
        }

        var snapshot = BuildPrototypeLongitudinal(student);
        var paper = snapshot.Papers.FirstOrDefault(p =>
            string.Equals(p.PaperSubmissionId.ToString("D"), paperSubmissionId?.Trim(), StringComparison.OrdinalIgnoreCase));
        if (paper is null)
        {
            return Task.FromResult<object>(new { found = false, error = "XQ_PAPER_NOT_FOUND" });
        }

        return Task.FromResult<object>(new
        {
            found = true,
            mode = "prototype",
            studentId = snapshot.StudentId.ToString("D"),
            paper = new
            {
                paperSubmissionId = paper.PaperSubmissionId.ToString("D"),
                paperDate = paper.PaperDate.ToString("yyyy-MM-dd"),
                paperKind = PaperEvidenceWireNames.ToWire(paper.PaperKind),
                paper.KindLabel,
                paper.Title,
                gradingSource = paper.GradingSource.ToString(),
                confirmationState = paper.ConfirmationState.ToString(),
                paper.QuestionCount,
                paper.MistakeCount,
                paper.ScoreEarned,
                paper.ScoreTotal,
                paper.DurationMinutes,
                paper.ExtractionSummary,
                pages = paper.Pages.Select(pg => new
                {
                    pg.PageIndex,
                    attachmentId = pg.AttachmentId.ToString("D"),
                    pg.HeaderRedacted,
                    pg.ContentType,
                    pg.ByteSize,
                }).ToArray(),
                mistakes = paper.Mistakes.Select(m => new
                {
                    mistakeId = m.MistakeId.ToString("D"),
                    m.PageIndex,
                    m.QuestionLabel,
                    m.TopicTags,
                    errorRootCause = PaperEvidenceWireNames.ToWire(m.ErrorRootCause),
                    errorRootCauseLabel = PaperEvidenceWireNames.ToChineseLabel(m.ErrorRootCause),
                    paperSectionPosition = m.PaperSectionPosition.ToString().ToLowerInvariant(),
                    paperSectionLabel = PaperEvidenceWireNames.ToChineseLabel(m.PaperSectionPosition),
                    m.PointsLost,
                    m.CorrectedOnPaper,
                    m.Summary,
                    m.Provenance,
                }).ToArray(),
            },
            deepLink = $"xueqing://student/{snapshot.StudentId:D}",
        });
    }

    public async Task<object> GetStudentLongitudinalMetricsAsync(
        string studentId,
        string? subjectProfileId,
        int windowDays,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var student = FindPrototypeStudent(studentId);
        if (student is null)
        {
            return new { found = false, error = "XQ_STUDENT_NOT_FOUND" };
        }

        var snapshot = BuildPrototypeLongitudinal(student);
        string? savedPlanJson = null;
        try
        {
            var scope = ResolveScope(student.Id, null, null, null);
            var opened = await _studyPlanDraftStore.OpenAsync(scope, cancellationToken);
            savedPlanJson = opened.Recovered?.Text;
        }
        catch
        {
            // Prototype metrics stay readable even without keyring; writing study plan still fails closed.
        }

        return new
        {
            found = true,
            mode = "prototype",
            contract = "student_paper_longitudinal_v1",
            studentId = snapshot.StudentId.ToString("D"),
            studentDisplayName = snapshot.StudentDisplayName,
            subjectKey = snapshot.SubjectKey,
            windowDays = Math.Clamp(windowDays, 7, 365),
            summary = snapshot.Summary,
            weeklyCadence = snapshot.WeeklyCadence.Select(w => new
            {
                weekStart = w.WeekStart.ToString("yyyy-MM-dd"),
                w.PaperCount,
                w.ConfirmedCount,
                w.MistakeCount,
                w.HomeworkCount,
                w.QuizCount,
                w.UnitTestCount,
                w.ExamCount,
            }).ToArray(),
            rootCauseBreakdown = snapshot.RootCauseBreakdown.Select(r => new
            {
                errorRootCause = PaperEvidenceWireNames.ToWire(r.ErrorRootCause),
                r.Label,
                r.Recent30dCount,
                r.PriorWindowCount,
                r.TotalCount,
            }).ToArray(),
            recurringTopics = snapshot.RecurringTopics.Select(t => new
            {
                t.TopicTag,
                t.OccurrenceCount,
                t.PaperCount,
                firstSeenDate = t.FirstSeenDate.ToString("yyyy-MM-dd"),
                lastSeenDate = t.LastSeenDate.ToString("yyyy-MM-dd"),
            }).ToArray(),
            hasLocalStudyPlanDraft = !string.IsNullOrWhiteSpace(savedPlanJson),
            latestLocalStudyPlanDraftJson = savedPlanJson,
            deepLink = $"xueqing://student/{snapshot.StudentId:D}",
        };
    }

    public async Task<object> SaveStudyPlanDraftAsync(
        string studentId,
        string title,
        string briefMarkdown,
        string? evidenceRefsJson,
        string? proposedActionsJson,
        int windowDays,
        int paperCountAnalyzed,
        string? organizationId,
        string? subjectProfileId,
        string? assignmentId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(briefMarkdown))
        {
            return new { saved = false, error = "XQ_INVALID_STUDY_PLAN_DRAFT", message = "Title and briefMarkdown are required." };
        }

        var scope = ResolveScope(studentId, organizationId, subjectProfileId, assignmentId);
        var payloadJson = System.Text.Json.JsonSerializer.Serialize(new
        {
            draftId = Guid.NewGuid().ToString("D"),
            title = title.Trim(),
            briefMarkdown = briefMarkdown.Trim(),
            evidenceRefsJson = string.IsNullOrWhiteSpace(evidenceRefsJson) ? "[]" : evidenceRefsJson,
            proposedActionsJson = string.IsNullOrWhiteSpace(proposedActionsJson) ? "[]" : proposedActionsJson,
            windowDays = Math.Clamp(windowDays, 7, 365),
            paperCountAnalyzed = Math.Max(0, paperCountAnalyzed),
            status = "draft",
            savedAtUtc = DateTimeOffset.UtcNow,
        });

        var opened = await _studyPlanDraftStore.OpenAsync(scope, cancellationToken);
        var saved = await _studyPlanDraftStore.SaveAsync(
            scope,
            opened.Epoch,
            payloadJson,
            DateTimeOffset.UtcNow,
            cancellationToken);

        return new
        {
            mode = "prototype",
            encrypted = true,
            saved,
            status = "draft",
            mutatedLearningCases = false,
            deepLink = $"xueqing://student/{scope.StudentId:D}",
            nextStep = "Study plan draft saved in encrypted local storage. Open the native Xueqing app via deepLink for teacher review and explicit adoption.",
        };
    }

    private StudentPaperLongitudinalSnapshot BuildPrototypeLongitudinal(StudentSummary student) =>
        PrototypePaperLongitudinalFixtureFactory.Create(
            ResolvePrototypeStudentUuid(student.Id),
            student.DisplayName,
            student.PrimarySubject,
            PrototypeOrgId,
            PrototypeSubjectProfileId,
            PrototypeAssignmentId);

    public void Dispose()
    {
    }
}

/// <summary>
/// Production MCP backend reusing the exact same DeploymentProfile, OS keyring RefreshTokenVault,
/// ProviderAuthCoordinator, Postgrest readers, and scoped encrypted ObservationDraftStore as the desktop apps.
/// </summary>
internal sealed class ProductionXueqingMcpBackend : IXueqingMcpBackend
{
    private readonly string _dataRoot;
    private readonly DeploymentProfile? _profile;
    private readonly string? _configurationError;
    private readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(20) };
    private readonly ProviderSessionTokenSource? _tokenSource;
    private readonly ProviderAuthCoordinator? _authCoordinator;
    private readonly PostgrestPersonalBootstrapReader? _bootstrapReader;
    private readonly PostgrestPersonalTodayActionsReader? _todayReader;
    private readonly PostgrestStudentLearningFocusReader? _focusReader;
    private readonly PostgrestStudentRecentObservationsReader? _recentReader;
    private readonly PostgrestStudentLearningCasesReader? _casesReader;
    private readonly PostgrestPaperEvidenceClient? _paperClient;

    public ProductionXueqingMcpBackend(XueqingMcpOptions options)
    {
        _dataRoot = XueqingMcpHostHelpers.ResolveDataRoot(options.DataRoot);
        var profilePath = options.ProfilePath
            ?? Path.Combine(AppContext.BaseDirectory, "deployment_profile.json");

        if (!File.Exists(profilePath))
        {
            _configurationError =
                $"Production deployment_profile.json not found at {profilePath}. Pass --profile <path> or run with --prototype.";
            return;
        }

        try
        {
            _profile = DeploymentProfileParser.Parse(File.ReadAllText(profilePath));
            _tokenSource = new ProviderSessionTokenSource(_profile.EnvironmentId, _profile.TrustDomainId);
            var vault = XueqingMcpHostHelpers.CreatePlatformRefreshTokenVault(
                _dataRoot,
                _profile.EnvironmentId,
                _profile.TrustDomainId);
            var transport = new SupabaseAuthTransport(_httpClient, _profile.ProjectOrigin, _profile.PublishableKey);
            _authCoordinator = new ProviderAuthCoordinator(_tokenSource, vault, transport);

            _bootstrapReader = new PostgrestPersonalBootstrapReader(_httpClient, _profile.ProjectOrigin, _profile.PublishableKey, _tokenSource.GetCurrentAccessTokenAsync);
            _todayReader = new PostgrestPersonalTodayActionsReader(_httpClient, _profile.ProjectOrigin, _profile.PublishableKey, _tokenSource.GetCurrentAccessTokenAsync);
            _focusReader = new PostgrestStudentLearningFocusReader(_httpClient, _profile.ProjectOrigin, _profile.PublishableKey, _tokenSource.GetCurrentAccessTokenAsync);
            _recentReader = new PostgrestStudentRecentObservationsReader(_httpClient, _profile.ProjectOrigin, _profile.PublishableKey, _tokenSource.GetCurrentAccessTokenAsync);
            _casesReader = new PostgrestStudentLearningCasesReader(_httpClient, _profile.ProjectOrigin, _profile.PublishableKey, _tokenSource.GetCurrentAccessTokenAsync);
            _paperClient = new PostgrestPaperEvidenceClient(_httpClient, _profile.ProjectOrigin, _profile.PublishableKey, _tokenSource.GetCurrentAccessTokenAsync);
        }
        catch (Exception ex)
        {
            _configurationError = ex.Message;
        }
    }

    public async Task<object> GetStatusAsync(CancellationToken cancellationToken)
    {
        if (_configurationError is not null || _authCoordinator is null || _profile is null)
        {
            return new
            {
                contract = "xueqing_mcp_v1",
                mode = "production",
                platform = XueqingMcpHostHelpers.PlatformName,
                sessionState = "configuration_unavailable",
                error = _configurationError ?? "Production runtime unavailable.",
            };
        }

        var outcome = await EnsureSessionAsync(cancellationToken);
        return new
        {
            contract = "xueqing_mcp_v1",
            mode = "production",
            platform = XueqingMcpHostHelpers.PlatformName,
            environmentId = _profile.EnvironmentId,
            profileId = _profile.ProfileId,
            sessionState = outcome.ToString(),
            writePolicy = "read_local_observation_draft_and_server_study_plan_draft_only",
        };
    }

    public async Task<object> ListStudentsAsync(string? query, int page, int pageSize, CancellationToken cancellationToken)
    {
        var (bootstrap, error) = await LoadBootstrapAsync(cancellationToken);
        if (error is not null || bootstrap is null)
        {
            return error!;
        }

        var clampedPage = Math.Max(1, page);
        var clampedSize = Math.Clamp(pageSize, 1, 50);
        var grouped = bootstrap.TeachingContexts
            .GroupBy(c => c.StudentId)
            .Select(g => new
            {
                studentId = g.Key.ToString("D"),
                displayName = g.First().StudentDisplayName,
                subjects = g.Select(x => x.SubjectKey).Distinct(StringComparer.Ordinal).ToArray(),
                teachingContextCount = g.Count(),
                deepLink = $"xueqing://student/{g.Key:D}",
            })
            .Where(s => string.IsNullOrWhiteSpace(query) ||
                        s.displayName.Contains(query.Trim(), StringComparison.OrdinalIgnoreCase) ||
                        s.subjects.Any(sub => sub.Contains(query.Trim(), StringComparison.OrdinalIgnoreCase)))
            .OrderBy(s => s.displayName, StringComparer.Ordinal)
            .ToArray();

        return new
        {
            mode = "production",
            page = clampedPage,
            pageSize = clampedSize,
            totalMatches = grouped.Length,
            hasMore = clampedPage * clampedSize < grouped.Length,
            students = grouped.Skip((clampedPage - 1) * clampedSize).Take(clampedSize).ToArray(),
        };
    }

    public async Task<object> GetTodayActionsAsync(string? bucket, int limit, CancellationToken cancellationToken)
    {
        var (bootstrap, error) = await LoadBootstrapAsync(cancellationToken);
        if (error is not null || bootstrap is null || _todayReader is null)
        {
            return error!;
        }

        var result = await _todayReader.ReadAsync(bootstrap.ActorAppUserId, cancellationToken);
        if (!result.IsSuccess || result.Snapshot is null)
        {
            return new { ok = false, error = result.Failure?.Code ?? "XQ_TODAY_READ_FAILED" };
        }

        var normalizedBucket = bucket?.Trim().ToLowerInvariant();
        var actions = result.Snapshot.Actions
            .Where(a => string.IsNullOrEmpty(normalizedBucket) ||
                        a.DueBucket.ToString().Equals(normalizedBucket, StringComparison.OrdinalIgnoreCase))
            .Take(Math.Clamp(limit, 1, 50))
            .Select(a => new
            {
                organizationId = a.OrganizationId.ToString("D"),
                organizationName = a.OrganizationName,
                studentId = a.StudentId.ToString("D"),
                a.StudentDisplayName,
                subjectProfileId = a.SubjectProfileId.ToString("D"),
                a.SubjectKey,
                caseId = a.CaseId.ToString("D"),
                a.CaseTitle,
                caseState = a.CaseState.ToString(),
                actionId = a.ActionId.ToString("D"),
                a.ActionText,
                dueOn = a.DueOn?.ToString("yyyy-MM-dd"),
                dueBucket = a.DueBucket.ToString().ToLowerInvariant(),
                deepLink = $"xueqing://learning/{a.CaseId:D}",
            })
            .ToArray();

        return new
        {
            mode = "production",
            generatedAtServer = result.Snapshot.GeneratedAtServer,
            hasMore = result.Snapshot.HasMore,
            count = actions.Length,
            actions,
            deepLink = "xueqing://today",
        };
    }

    public async Task<object> GetStudentContextAsync(string studentId, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(studentId, out var studentGuid) || studentGuid == Guid.Empty)
        {
            return new { ok = false, error = "XQ_INVALID_STUDENT_ID", message = "studentId must be a valid UUID." };
        }

        var (bootstrap, error) = await LoadBootstrapAsync(cancellationToken);
        if (error is not null || bootstrap is null || _focusReader is null || _recentReader is null)
        {
            return error!;
        }

        var contexts = bootstrap.TeachingContexts.Where(c => c.StudentId == studentGuid).ToArray();
        if (contexts.Length == 0)
        {
            return new { found = false, error = "XQ_STUDENT_NOT_AUTHORIZED" };
        }

        var first = contexts[0];
        var focus = await _focusReader.ReadAsync(new StudentLearningScope(first.OrganizationId, first.StudentId, first.SubjectProfileId), bootstrap.ActorAppUserId, cancellationToken);
        var recent = await _recentReader.ReadAsync(first.ObservationScope, bootstrap.ActorAppUserId, cancellationToken);

        return new
        {
            found = true,
            mode = "production",
            studentId = studentGuid.ToString("D"),
            studentDisplayName = first.StudentDisplayName,
            teachingContexts = contexts.Select(c => new
            {
                organizationId = c.OrganizationId.ToString("D"),
                subjectProfileId = c.SubjectProfileId.ToString("D"),
                c.SubjectKey,
                assignmentId = c.AssignmentId.ToString("D"),
            }).ToArray(),
            currentFocus = focus.IsSuccess && focus.Snapshot is not null
                ? focus.Snapshot.Cases.Select(c => new
                {
                    caseId = c.CaseId.ToString("D"),
                    c.Title,
                    state = c.State.ToString(),
                    nextAction = c.PrimaryAction.ActionText,
                    dueOn = c.PrimaryAction.DueOn?.ToString("yyyy-MM-dd"),
                }).ToArray()
                : Array.Empty<object>(),
            recentObservations = recent.IsSuccess && recent.Snapshot is not null
                ? recent.Snapshot.Observations.Select(o => new
                {
                    observationId = o.ObservationId.ToString("D"),
                    o.RawText,
                    o.CreatedAtServer,
                }).ToArray()
                : Array.Empty<object>(),
            deepLink = $"xueqing://student/{studentGuid:D}",
        };
    }

    public async Task<object> GetStudentCasesAsync(
        string studentId,
        string? subjectProfileId,
        int timelineLimit,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(studentId, out var studentGuid) || studentGuid == Guid.Empty)
        {
            return new { ok = false, error = "XQ_INVALID_STUDENT_ID" };
        }

        var (bootstrap, error) = await LoadBootstrapAsync(cancellationToken);
        if (error is not null || bootstrap is null || _casesReader is null)
        {
            return error!;
        }

        var matches = bootstrap.TeachingContexts.Where(c =>
            c.StudentId == studentGuid &&
            (string.IsNullOrWhiteSpace(subjectProfileId) ||
             string.Equals(c.SubjectProfileId.ToString("D"), subjectProfileId.Trim(), StringComparison.OrdinalIgnoreCase))).ToArray();
        if (matches.Length == 0)
        {
            return new { found = false, error = "XQ_TEACHING_CONTEXT_NOT_FOUND" };
        }
        if (matches.Length > 1)
        {
            return BuildDisambiguationError(matches);
        }

        var context = matches[0];

        var result = await _casesReader.ReadAsync(
            new StudentLearningScope(context.OrganizationId, context.StudentId, context.SubjectProfileId),
            bootstrap.ActorAppUserId,
            cancellationToken);
        if (!result.IsSuccess || result.Snapshot is null)
        {
            return new { ok = false, error = result.Failure?.Code ?? "XQ_CASES_READ_FAILED" };
        }

        return new
        {
            mode = "production",
            studentId = studentGuid.ToString("D"),
            context.StudentDisplayName,
            context.SubjectKey,
            hasMore = result.Snapshot.HasMore,
            cases = result.Snapshot.Cases.Take(Math.Clamp(timelineLimit, 1, 50)).Select(c => new
            {
                caseId = c.CaseId.ToString("D"),
                c.Title,
                state = c.State.ToString(),
                c.Version,
                c.IsCurrentActorResponsibility,
                nextAction = c.PrimaryAction?.ActionText,
                dueOn = c.PrimaryAction?.DueOn?.ToString("yyyy-MM-dd"),
                deepLink = $"xueqing://learning/{c.CaseId:D}",
            }).ToArray(),
        };
    }

    public async Task<object> GetObservationDraftAsync(
        string studentId,
        string? organizationId,
        string? subjectProfileId,
        string? assignmentId,
        CancellationToken cancellationToken)
    {
        var (bootstrap, scope, error) = await ResolveAuthoritativeDraftScopeAsync(studentId, organizationId, subjectProfileId, assignmentId, cancellationToken);
        if (error is not null || bootstrap is null || scope is null)
        {
            return error!;
        }

        var store = OpenScopedDraftStore(bootstrap.ActorAppUserId, scope.OrganizationId);
        var opened = await store.OpenAsync(scope, cancellationToken);
        return new
        {
            mode = "production",
            encrypted = true,
            epoch = opened.Epoch,
            hasRecoveredDraft = opened.Recovered is not null,
            text = opened.Recovered?.Text ?? string.Empty,
            updatedAt = opened.Recovered?.UpdatedAt,
            deepLink = $"xueqing://student/{scope.StudentId:D}",
        };
    }

    public async Task<object> SaveObservationDraftAsync(
        string studentId,
        long expectedEpoch,
        string text,
        string? organizationId,
        string? subjectProfileId,
        string? assignmentId,
        CancellationToken cancellationToken)
    {
        var (bootstrap, scope, error) = await ResolveAuthoritativeDraftScopeAsync(studentId, organizationId, subjectProfileId, assignmentId, cancellationToken);
        if (error is not null || bootstrap is null || scope is null)
        {
            return error!;
        }

        var store = OpenScopedDraftStore(bootstrap.ActorAppUserId, scope.OrganizationId);
        var saved = await store.SaveAsync(scope, expectedEpoch, text ?? string.Empty, DateTimeOffset.UtcNow, cancellationToken);
        return new
        {
            mode = "production",
            encrypted = true,
            saved,
            conflict = !saved,
            submittedToServer = false,
            expectedEpoch,
            deepLink = $"xueqing://student/{scope.StudentId:D}",
        };
    }

    public async Task<object> ListStudentPapersAsync(
        string studentId,
        string? subjectProfileId,
        int windowDays,
        int limit,
        CancellationToken cancellationToken)
    {
        var (bootstrap, scope, error) = await ResolveAuthoritativeDraftScopeAsync(studentId, null, subjectProfileId, null, cancellationToken);
        if (error is not null || bootstrap is null || scope is null || _paperClient is null)
        {
            return error ?? new { ok = false, error = "XQ_CLIENT_UNAVAILABLE" };
        }

        var read = await _paperClient.ReadAsync(
            new StudentLearningScope(scope.OrganizationId, scope.StudentId, scope.SubjectProfileId),
            bootstrap.ActorAppUserId,
            windowDays,
            limit,
            cancellationToken);
        if (!read.IsSuccess || read.Snapshot is null)
        {
            return new { ok = false, error = read.Failure?.Code ?? "XQ_PAPER_READ_FAILED" };
        }

        var s = read.Snapshot;
        return new
        {
            found = true,
            mode = "production",
            studentId = s.StudentId.ToString("D"),
            studentDisplayName = s.StudentDisplayName,
            subjectKey = s.SubjectKey,
            windowDays = s.WindowDays,
            count = s.Papers.Count,
            papers = s.Papers.Select(p => new
            {
                paperSubmissionId = p.PaperSubmissionId.ToString("D"),
                paperDate = p.PaperDate.ToString("yyyy-MM-dd"),
                paperKind = PaperEvidenceWireNames.ToWire(p.PaperKind),
                p.KindLabel,
                p.Title,
                gradingSource = p.GradingSource.ToString(),
                confirmationState = p.ConfirmationState.ToString(),
                p.QuestionCount,
                p.MistakeCount,
                p.ScoreEarned,
                p.ScoreTotal,
                p.DurationMinutes,
                pageCount = p.Pages.Count,
                p.ExtractionSummary,
            }).ToArray(),
            deepLink = $"xueqing://student/{s.StudentId:D}",
        };
    }

    public async Task<object> GetPaperDetailAsync(
        string studentId,
        string paperSubmissionId,
        string? subjectProfileId,
        CancellationToken cancellationToken)
    {
        var (bootstrap, scope, error) = await ResolveAuthoritativeDraftScopeAsync(studentId, null, subjectProfileId, null, cancellationToken);
        if (error is not null || bootstrap is null || scope is null || _paperClient is null)
        {
            return error ?? new { ok = false, error = "XQ_CLIENT_UNAVAILABLE" };
        }

        var read = await _paperClient.ReadAsync(
            new StudentLearningScope(scope.OrganizationId, scope.StudentId, scope.SubjectProfileId),
            bootstrap.ActorAppUserId,
            365,
            50,
            cancellationToken);
        if (!read.IsSuccess || read.Snapshot is null)
        {
            return new { ok = false, error = read.Failure?.Code ?? "XQ_PAPER_READ_FAILED" };
        }

        var paper = read.Snapshot.Papers.FirstOrDefault(p =>
            string.Equals(p.PaperSubmissionId.ToString("D"), paperSubmissionId?.Trim(), StringComparison.OrdinalIgnoreCase));
        if (paper is null)
        {
            return new { found = false, error = "XQ_PAPER_NOT_FOUND" };
        }

        return new
        {
            found = true,
            mode = "production",
            studentId = read.Snapshot.StudentId.ToString("D"),
            paper,
            deepLink = $"xueqing://student/{read.Snapshot.StudentId:D}",
        };
    }

    public async Task<object> GetStudentLongitudinalMetricsAsync(
        string studentId,
        string? subjectProfileId,
        int windowDays,
        CancellationToken cancellationToken)
    {
        var (bootstrap, scope, error) = await ResolveAuthoritativeDraftScopeAsync(studentId, null, subjectProfileId, null, cancellationToken);
        if (error is not null || bootstrap is null || scope is null || _paperClient is null)
        {
            return error ?? new { ok = false, error = "XQ_CLIENT_UNAVAILABLE" };
        }

        var read = await _paperClient.ReadAsync(
            new StudentLearningScope(scope.OrganizationId, scope.StudentId, scope.SubjectProfileId),
            bootstrap.ActorAppUserId,
            windowDays,
            30,
            cancellationToken);
        if (!read.IsSuccess || read.Snapshot is null)
        {
            return new { ok = false, error = read.Failure?.Code ?? "XQ_PAPER_READ_FAILED" };
        }

        var s = read.Snapshot;
        return new
        {
            found = true,
            mode = "production",
            contract = "student_paper_longitudinal_v1",
            studentId = s.StudentId.ToString("D"),
            s.StudentDisplayName,
            s.SubjectKey,
            s.WindowDays,
            s.Summary,
            s.WeeklyCadence,
            s.RootCauseBreakdown,
            s.RecurringTopics,
            s.RecentStudyPlanDrafts,
            deepLink = $"xueqing://student/{s.StudentId:D}",
        };
    }

    public async Task<object> SaveStudyPlanDraftAsync(
        string studentId,
        string title,
        string briefMarkdown,
        string? evidenceRefsJson,
        string? proposedActionsJson,
        int windowDays,
        int paperCountAnalyzed,
        string? organizationId,
        string? subjectProfileId,
        string? assignmentId,
        CancellationToken cancellationToken)
    {
        var (_, scope, error) = await ResolveAuthoritativeDraftScopeAsync(studentId, organizationId, subjectProfileId, assignmentId, cancellationToken);
        if (error is not null || scope is null || _paperClient is null)
        {
            return error ?? new { ok = false, error = "XQ_CLIENT_UNAVAILABLE" };
        }

        var save = await _paperClient.SaveDraftAsync(
            new SaveStudyPlanDraftRequest(
                Guid.NewGuid(),
                scope.OrganizationId,
                scope.StudentId,
                scope.SubjectProfileId,
                scope.AssignmentId,
                title,
                briefMarkdown,
                string.IsNullOrWhiteSpace(evidenceRefsJson) ? "[]" : evidenceRefsJson,
                string.IsNullOrWhiteSpace(proposedActionsJson) ? "[]" : proposedActionsJson,
                Math.Clamp(windowDays, 7, 365),
                Math.Max(0, paperCountAnalyzed)),
            cancellationToken);

        if (!save.IsSuccess || save.Receipt is null)
        {
            return new { saved = false, error = save.Failure?.Code ?? "XQ_SAVE_STUDY_PLAN_FAILED" };
        }

        return new
        {
            mode = "production",
            saved = true,
            draftId = save.Receipt.DraftId.ToString("D"),
            status = save.Receipt.Status,
            version = save.Receipt.Version,
            mutatedLearningCases = false,
            deepLink = $"xueqing://student/{scope.StudentId:D}",
        };
    }

    private async Task<ProviderRefreshOutcome> EnsureSessionAsync(CancellationToken cancellationToken)
    {
        if (_tokenSource?.Snapshot.Status == ProviderSessionStatus.Usable)
        {
            return ProviderRefreshOutcome.Usable;
        }

        return await _authCoordinator!.RestoreAsync(cancellationToken);
    }

    private async Task<object?> RequireUsableSessionAsync(CancellationToken cancellationToken)
    {
        if (_configurationError is not null || _authCoordinator is null)
        {
            return new { ok = false, error = "XQ_CONFIGURATION_UNAVAILABLE", message = _configurationError };
        }

        var outcome = await EnsureSessionAsync(cancellationToken);
        if (outcome == ProviderRefreshOutcome.Usable)
        {
            return null;
        }

        return new
        {
            ok = false,
            error = "XQ_AUTH_REQUIRED",
            sessionState = outcome.ToString(),
            message = "Please sign in using the native Xueqing desktop app first.",
        };
    }

    private async Task<(PersonalBootstrapSnapshot? Snapshot, object? Error)> LoadBootstrapAsync(CancellationToken cancellationToken)
    {
        var authError = await RequireUsableSessionAsync(cancellationToken);
        if (authError is not null || _bootstrapReader is null)
        {
            return (null, authError);
        }

        var result = await _bootstrapReader.ReadAsync(cancellationToken);
        if (!result.IsSuccess || result.Snapshot is null)
        {
            return (null, new { ok = false, error = result.Failure?.Code ?? "XQ_BOOTSTRAP_FAILED" });
        }

        return (result.Snapshot, null);
    }

    private async Task<(PersonalBootstrapSnapshot? Bootstrap, ObservationDraftScope? Scope, object? Error)> ResolveAuthoritativeDraftScopeAsync(
        string studentId,
        string? organizationId,
        string? subjectProfileId,
        string? assignmentId,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(studentId, out var studentGuid) || studentGuid == Guid.Empty)
        {
            return (null, null, new { ok = false, error = "XQ_INVALID_STUDENT_ID" });
        }

        var (bootstrap, error) = await LoadBootstrapAsync(cancellationToken);
        if (error is not null || bootstrap is null)
        {
            return (null, null, error);
        }

        var matches = bootstrap.TeachingContexts.Where(c =>
            c.StudentId == studentGuid &&
            (string.IsNullOrWhiteSpace(organizationId) || string.Equals(c.OrganizationId.ToString("D"), organizationId.Trim(), StringComparison.OrdinalIgnoreCase)) &&
            (string.IsNullOrWhiteSpace(subjectProfileId) || string.Equals(c.SubjectProfileId.ToString("D"), subjectProfileId.Trim(), StringComparison.OrdinalIgnoreCase)) &&
            (string.IsNullOrWhiteSpace(assignmentId) || string.Equals(c.AssignmentId.ToString("D"), assignmentId.Trim(), StringComparison.OrdinalIgnoreCase))).ToArray();

        if (matches.Length == 0)
        {
            return (null, null, new { ok = false, error = "XQ_TEACHING_CONTEXT_NOT_AUTHORIZED" });
        }

        if (matches.Length > 1)
        {
            return (null, null, BuildDisambiguationError(matches));
        }

        var context = matches[0];
        return (bootstrap, new ObservationDraftScope(context.OrganizationId, context.StudentId, context.SubjectProfileId, context.AssignmentId), null);
    }

    private static object BuildDisambiguationError(IReadOnlyList<PersonalTeachingContext> candidates) => new
    {
        ok = false,
        found = false,
        error = "XQ_SUBJECT_DISAMBIGUATION_REQUIRED",
        message = "Multiple authorized teaching contexts match this student. Pass subjectProfileId (and organizationId when needed) explicitly.",
        candidates = candidates.Select(c => new
        {
            organizationId = c.OrganizationId.ToString("D"),
            subjectProfileId = c.SubjectProfileId.ToString("D"),
            subjectKey = c.SubjectKey,
            assignmentId = c.AssignmentId.ToString("D"),
        }).ToArray(),
    };

    private SqliteObservationDraftStore OpenScopedDraftStore(Guid actorAppUserId, Guid organizationId)
    {
        string dbPath;
        if (OperatingSystem.IsLinux())
        {
            var installId = LinuxLocalStatePaths.LoadOrCreateInstallationId(_dataRoot);
            dbPath = LinuxLocalStatePaths.GetObservationDraftDatabasePath(
                _profile!.EnvironmentId,
                actorAppUserId.ToString("D"),
                organizationId.ToString("D"),
                installId,
                _dataRoot);
        }
        else
        {
            var scopeDir = Path.Combine(
                _dataRoot,
                "scoped-local-data",
                "v1",
                WindowsLocalScopeIdentity.ComputeOrganizationScopeDirectoryName(
                    _profile!.EnvironmentId,
                    actorAppUserId.ToString("D"),
                    organizationId.ToString("D")));
            Directory.CreateDirectory(scopeDir);
            dbPath = Path.Combine(scopeDir, "observation-draft.db");
        }

        return new SqliteObservationDraftStore(dbPath, XueqingMcpHostHelpers.CreatePlatformKeyStore(dbPath));
    }

    public void Dispose() => _httpClient.Dispose();
}

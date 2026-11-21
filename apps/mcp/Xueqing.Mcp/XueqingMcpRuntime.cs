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

    public PrototypeXueqingMcpBackend(string? explicitDataRoot)
    {
        var dataRoot = XueqingMcpHostHelpers.ResolveDataRoot(explicitDataRoot);
        _draftDbPath = Path.Combine(dataRoot, "mcp-prototype", "observation-draft.db");
        var keyStore = XueqingMcpHostHelpers.CreatePlatformKeyStore(_draftDbPath);
        _draftStore = new SqliteObservationDraftStore(_draftDbPath, keyStore);
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
            writePolicy = "read_and_encrypted_local_draft_only",
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
            writePolicy = "read_and_encrypted_local_draft_only",
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

        var context = bootstrap.TeachingContexts.FirstOrDefault(c =>
            c.StudentId == studentGuid &&
            (string.IsNullOrWhiteSpace(subjectProfileId) ||
             string.Equals(c.SubjectProfileId.ToString("D"), subjectProfileId.Trim(), StringComparison.OrdinalIgnoreCase)));
        if (context is null)
        {
            return new { found = false, error = "XQ_TEACHING_CONTEXT_NOT_FOUND" };
        }

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

        var context = bootstrap.TeachingContexts.FirstOrDefault(c =>
            c.StudentId == studentGuid &&
            (string.IsNullOrWhiteSpace(organizationId) || string.Equals(c.OrganizationId.ToString("D"), organizationId.Trim(), StringComparison.OrdinalIgnoreCase)) &&
            (string.IsNullOrWhiteSpace(subjectProfileId) || string.Equals(c.SubjectProfileId.ToString("D"), subjectProfileId.Trim(), StringComparison.OrdinalIgnoreCase)) &&
            (string.IsNullOrWhiteSpace(assignmentId) || string.Equals(c.AssignmentId.ToString("D"), assignmentId.Trim(), StringComparison.OrdinalIgnoreCase)));

        if (context is null)
        {
            return (null, null, new { ok = false, error = "XQ_TEACHING_CONTEXT_NOT_AUTHORIZED" });
        }

        return (bootstrap, new ObservationDraftScope(context.OrganizationId, context.StudentId, context.SubjectProfileId, context.AssignmentId), null);
    }

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

using System.ComponentModel;
using System.Text.Encodings.Web;
using System.Text.Json;
using ModelContextProtocol.Server;
using Xueqing.Windows.Core.Agent;

namespace Xueqing.Mcp;

[McpServerToolType]
public sealed class XueqingMcpTools(IXueqingMcpBackend backend)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    [McpServerTool(Name = "xueqing_status", ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = false)]
    [Description("Get Xueqing MCP runtime status: mode (prototype/production), host platform, session readiness, and write policy. Never returns secrets or tokens.")]
    public async Task<string> GetStatusAsync(CancellationToken cancellationToken = default) =>
        Serialize(await backend.GetStatusAsync(cancellationToken));

    [McpServerTool(Name = "xueqing_list_students", ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = false)]
    [Description("Search and paginate authorized students in the personal teaching workspace.")]
    public async Task<string> ListStudentsAsync(
        [Description("Optional search keyword matching student name, code, or subject.")] string? query = null,
        [Description("1-based page number (default 1).")] int page = 1,
        [Description("Items per page (1 to 50, default 10).")] int pageSize = 10,
        CancellationToken cancellationToken = default) =>
        Serialize(await backend.ListStudentsAsync(query, page, pageSize, cancellationToken));

    [McpServerTool(Name = "xueqing_get_today_actions", ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = false)]
    [Description("Retrieve explicit pending teaching actions in the Today queue, optionally filtered by bucket (overdue, today, undated, future).")]
    public async Task<string> GetTodayActionsAsync(
        [Description("Optional bucket filter: overdue, today, undated, or future.")] string? bucket = null,
        [Description("Maximum actions to return (1 to 50, default 20).")] int limit = 20,
        CancellationToken cancellationToken = default) =>
        Serialize(await backend.GetTodayActionsAsync(bucket, limit, cancellationToken));

    [McpServerTool(Name = "xueqing_get_student_context", ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = false)]
    [Description("Retrieve teaching contexts, current focus, and recent observations for one student.")]
    public async Task<string> GetStudentContextAsync(
        [Description("Student UUID (or prototype student key/name in prototype mode).")] string studentId,
        CancellationToken cancellationToken = default) =>
        Serialize(await backend.GetStudentContextAsync(studentId, cancellationToken));

    [McpServerTool(Name = "xueqing_get_student_cases", ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = false)]
    [Description("Retrieve Learning Cases (title, state, responsibility, next action, due date, and timeline when available) for a student.")]
    public async Task<string> GetStudentCasesAsync(
        [Description("Student UUID (or prototype student key in prototype mode).")] string studentId,
        [Description("Optional subjectProfileId UUID to disambiguate multi-subject contexts.")] string? subjectProfileId = null,
        [Description("Maximum cases or timeline entries to return (1 to 50, default 20).")] int limit = 20,
        CancellationToken cancellationToken = default) =>
        Serialize(await backend.GetStudentCasesAsync(studentId, subjectProfileId, limit, cancellationToken));

    [McpServerTool(Name = "xueqing_get_observation_draft", ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = false)]
    [Description("Open and read the local encrypted observation draft and its current concurrency epoch for a student teaching context.")]
    public async Task<string> GetObservationDraftAsync(
        [Description("Student UUID.")] string studentId,
        [Description("Optional organization UUID (required in production if ambiguous).")] string? organizationId = null,
        [Description("Optional subject profile UUID.")] string? subjectProfileId = null,
        [Description("Optional teaching assignment UUID.")] string? assignmentId = null,
        CancellationToken cancellationToken = default) =>
        Serialize(await backend.GetObservationDraftAsync(studentId, organizationId, subjectProfileId, assignmentId, cancellationToken));

    [McpServerTool(Name = "xueqing_save_observation_draft", ReadOnly = false, Idempotent = false, Destructive = false, OpenWorld = false)]
    [Description("Save an AI-assisted classroom observation draft into local encrypted storage using optimistic expectedEpoch concurrency. Never submits to the server; the teacher reviews and submits in the native app.")]
    public async Task<string> SaveObservationDraftAsync(
        [Description("Student UUID.")] string studentId,
        [Description("Expected epoch previously returned by xueqing_get_observation_draft.")] long expectedEpoch,
        [Description("Draft observation text to store locally.")] string text,
        [Description("Optional organization UUID.")] string? organizationId = null,
        [Description("Optional subject profile UUID.")] string? subjectProfileId = null,
        [Description("Optional teaching assignment UUID.")] string? assignmentId = null,
        CancellationToken cancellationToken = default) =>
        Serialize(await backend.SaveObservationDraftAsync(studentId, expectedEpoch, text, organizationId, subjectProfileId, assignmentId, cancellationToken));

    [McpServerTool(Name = "xueqing_build_deep_link", ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = false)]
    [Description("Validate and format a canonical xueqing:// deep link (today, student, or learning) for opening in the native desktop app.")]
    public Task<string> BuildDeepLinkAsync(
        [Description("Target surface: today, student, or learning.")] string target,
        [Description("Canonical UUID required when target is student or learning.")] string? entityId = null)
    {
        var normalized = target?.Trim().ToLowerInvariant();
        Uri? candidate;
        switch (normalized)
        {
            case "today":
                candidate = new Uri("xueqing://today");
                break;
            case "student" when Guid.TryParse(entityId, out var studentGuid) && studentGuid != Guid.Empty:
                candidate = new Uri($"xueqing://student/{studentGuid:D}");
                break;
            case "learning" when Guid.TryParse(entityId, out var caseGuid) && caseGuid != Guid.Empty:
                candidate = new Uri($"xueqing://learning/{caseGuid:D}");
                break;
            default:
                return Task.FromResult(Serialize(new
                {
                    valid = false,
                    error = "XQ_INVALID_DEEP_LINK_TARGET",
                    message = "Supported targets: 'today', 'student' (with UUID entityId), or 'learning' (with UUID entityId).",
                }));
        }

        if (!AgentNavigationRequest.TryParse(candidate, out var parsed) || parsed is null)
        {
            return Task.FromResult(Serialize(new
            {
                valid = false,
                error = "XQ_INVALID_DEEP_LINK_URI",
            }));
        }

        return Task.FromResult(Serialize(new
        {
            valid = true,
            kind = parsed.Kind.ToString(),
            uri = parsed.ToUri().ToString(),
        }));
    }

    private static string Serialize(object payload) =>
        JsonSerializer.Serialize(payload, JsonOptions);
}

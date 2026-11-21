using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Xueqing.Windows.Core.Models;
using Xueqing.Windows.Core.Services;

namespace Xueqing.Windows.Infrastructure.Remote;

public sealed class PostgrestPaperEvidenceClient :
    IStudentPaperLongitudinalReader,
    IStudyPlanDraftWriter,
    IPaperAttachmentDownloader
{
    private const string LongitudinalRpcPath = "rest/v1/rpc/get_student_paper_longitudinal_v1";
    private const string SaveStudyPlanDraftRpcPath = "rest/v1/rpc/save_study_plan_draft_v1";
    private const string ExpectedBucketId = "teaching-attachments-v1";
    private const int MaxAttachmentBytes = 6 * 1024 * 1024;

    private static readonly HashSet<string> AllowedImageMimeTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg",
        "image/png",
        "image/webp",
    };

    private readonly HttpClient _httpClient;
    private readonly Uri _projectUri;
    private readonly Uri _longitudinalRpcUri;
    private readonly Uri _saveStudyPlanDraftRpcUri;
    private readonly string _apiKey;
    private readonly Func<CancellationToken, ValueTask<string?>> _accessTokenProvider;

    public PostgrestPaperEvidenceClient(
        HttpClient httpClient,
        Uri projectUri,
        string apiKey,
        Func<CancellationToken, ValueTask<string?>> accessTokenProvider)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _projectUri = projectUri ?? throw new ArgumentNullException(nameof(projectUri));
        _longitudinalRpcUri = LearningReadProviderSupport.BuildRpcUri(projectUri, LongitudinalRpcPath);
        _saveStudyPlanDraftRpcUri = LearningReadProviderSupport.BuildRpcUri(projectUri, SaveStudyPlanDraftRpcPath);
        _apiKey = string.IsNullOrWhiteSpace(apiKey)
            ? throw new ArgumentException("Reference-provider API key is required.", nameof(apiKey))
            : apiKey;
        _accessTokenProvider = accessTokenProvider ?? throw new ArgumentNullException(nameof(accessTokenProvider));
    }

    public async Task<StudentPaperLongitudinalReadResult> ReadAsync(
        StudentLearningScope scope,
        Guid expectedActorAppUserId,
        int windowDays = 90,
        int limit = 30,
        CancellationToken cancellationToken = default)
    {
        if (scope.OrganizationId == Guid.Empty ||
            scope.StudentId == Guid.Empty ||
            scope.SubjectProfileId == Guid.Empty)
        {
            return StudentPaperLongitudinalReadResult.Failed(
                LearningReadFailureKind.InvalidResponse,
                "XQ_CLIENT_SCOPE_INVALID");
        }

        if (expectedActorAppUserId == Guid.Empty)
        {
            return StudentPaperLongitudinalReadResult.Failed(
                LearningReadFailureKind.AuthenticationRequired,
                "XQ_CLIENT_ACTOR_REQUIRED");
        }

        var accessToken = await _accessTokenProvider(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            return StudentPaperLongitudinalReadResult.Failed(
                LearningReadFailureKind.AuthenticationRequired,
                "XQ_AUTH_REQUIRED");
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, _longitudinalRpcUri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.TryAddWithoutValidation("apikey", _apiKey);
        request.Content = JsonContent.Create(new
        {
            p_organization_id = scope.OrganizationId,
            p_student_id = scope.StudentId,
            p_subject_profile_id = scope.SubjectProfileId,
            p_window_days = Math.Clamp(windowDays, 7, 365),
            p_limit = Math.Clamp(limit, 1, 50),
        });

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return StudentPaperLongitudinalReadResult.Failed(LearningReadFailureKind.Transient, "XQ_NETWORK_TIMEOUT");
        }
        catch (HttpRequestException)
        {
            return StudentPaperLongitudinalReadResult.Failed(LearningReadFailureKind.Transient, "XQ_NETWORK_UNAVAILABLE");
        }

        using (response)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                try
                {
                    return StudentPaperLongitudinalReadResult.Success(
                        PaperEvidenceJsonParser.ParseLongitudinal(body, scope, expectedActorAppUserId));
                }
                catch (JsonException)
                {
                    return StudentPaperLongitudinalReadResult.Failed(
                        LearningReadFailureKind.InvalidResponse,
                        "XQ_PROJECTION_JSON_INVALID");
                }
                catch (InvalidDataException)
                {
                    return StudentPaperLongitudinalReadResult.Failed(
                        LearningReadFailureKind.InvalidResponse,
                        "XQ_PROJECTION_CONTRACT_INVALID");
                }
            }

            var (kind, code) = LearningReadProviderSupport.MapFailure(response.StatusCode, body);
            return StudentPaperLongitudinalReadResult.Failed(kind, code);
        }
    }

    public async Task<SaveStudyPlanDraftResult> SaveDraftAsync(
        SaveStudyPlanDraftRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var accessToken = await _accessTokenProvider(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            return SaveStudyPlanDraftResult.Failed(LearningReadFailureKind.AuthenticationRequired, "XQ_AUTH_REQUIRED");
        }

        JsonElement evidenceRefs;
        JsonElement proposedActions;
        try
        {
            using var refsDoc = JsonDocument.Parse(string.IsNullOrWhiteSpace(request.EvidenceRefsJson) ? "[]" : request.EvidenceRefsJson);
            using var actionsDoc = JsonDocument.Parse(string.IsNullOrWhiteSpace(request.ProposedActionsJson) ? "[]" : request.ProposedActionsJson);
            evidenceRefs = refsDoc.RootElement.Clone();
            proposedActions = actionsDoc.RootElement.Clone();
        }
        catch (JsonException)
        {
            return SaveStudyPlanDraftResult.Failed(LearningReadFailureKind.InvalidResponse, "XQ_INVALID_STUDY_PLAN_DRAFT");
        }

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, _saveStudyPlanDraftRpcUri);
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        httpRequest.Headers.TryAddWithoutValidation("apikey", _apiKey);
        httpRequest.Content = JsonContent.Create(new
        {
            p_operation_id = request.OperationId,
            p_organization_id = request.OrganizationId,
            p_student_id = request.StudentId,
            p_subject_profile_id = request.SubjectProfileId,
            p_assignment_id = request.AssignmentId,
            p_title = request.Title,
            p_brief_markdown = request.BriefMarkdown,
            p_evidence_refs = evidenceRefs,
            p_proposed_actions = proposedActions,
            p_window_days = request.WindowDays,
            p_paper_count_analyzed = request.PaperCountAnalyzed,
        });

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(
                httpRequest,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return SaveStudyPlanDraftResult.Failed(LearningReadFailureKind.Transient, "XQ_NETWORK_TIMEOUT");
        }
        catch (HttpRequestException)
        {
            return SaveStudyPlanDraftResult.Failed(LearningReadFailureKind.Transient, "XQ_NETWORK_UNAVAILABLE");
        }

        using (response)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                try
                {
                    return SaveStudyPlanDraftResult.Success(
                        PaperEvidenceJsonParser.ParseSaveStudyPlanDraftReceipt(body, request.OperationId));
                }
                catch (Exception ex) when (ex is JsonException or InvalidDataException)
                {
                    return SaveStudyPlanDraftResult.Failed(
                        LearningReadFailureKind.InvalidResponse,
                        "XQ_RECEIPT_CONTRACT_INVALID");
                }
            }

            var (kind, code) = LearningReadProviderSupport.MapFailure(response.StatusCode, body);
            return SaveStudyPlanDraftResult.Failed(kind, code);
        }
    }

    public async Task<AttachmentDownloadResult> DownloadImageAsync(
        string bucketId,
        string objectName,
        CancellationToken cancellationToken = default)
    {
        if (!string.Equals(bucketId, ExpectedBucketId, StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(objectName) ||
            objectName.Contains("..", StringComparison.Ordinal) ||
            !objectName.StartsWith("v1/org/", StringComparison.Ordinal))
        {
            return AttachmentDownloadResult.Failed(
                LearningReadFailureKind.InvalidResponse,
                "XQ_INVALID_ATTACHMENT_OBJECT_NAME");
        }

        var accessToken = await _accessTokenProvider(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            return AttachmentDownloadResult.Failed(
                LearningReadFailureKind.AuthenticationRequired,
                "XQ_AUTH_REQUIRED");
        }

        var encodedSegments = string.Join(
            "/",
            objectName.Split('/', StringSplitOptions.RemoveEmptyEntries).Select(Uri.EscapeDataString));
        var objectUri = LearningReadProviderSupport.BuildRpcUri(
            _projectUri,
            $"storage/v1/object/authenticated/{ExpectedBucketId}/{encodedSegments}");

        using var request = new HttpRequestMessage(HttpMethod.Get, objectUri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.TryAddWithoutValidation("apikey", _apiKey);

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return AttachmentDownloadResult.Failed(LearningReadFailureKind.Transient, "XQ_NETWORK_TIMEOUT");
        }
        catch (HttpRequestException)
        {
            return AttachmentDownloadResult.Failed(LearningReadFailureKind.Transient, "XQ_NETWORK_UNAVAILABLE");
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                var (kind, code) = LearningReadProviderSupport.MapFailure(response.StatusCode, body);
                return AttachmentDownloadResult.Failed(kind, code);
            }

            var contentType = response.Content.Headers.ContentType?.MediaType ?? string.Empty;
            if (!AllowedImageMimeTypes.Contains(contentType))
            {
                return AttachmentDownloadResult.Failed(
                    LearningReadFailureKind.InvalidResponse,
                    "XQ_UNSUPPORTED_ATTACHMENT_MIME");
            }

            if (response.Content.Headers.ContentLength is > MaxAttachmentBytes)
            {
                return AttachmentDownloadResult.Failed(
                    LearningReadFailureKind.InvalidResponse,
                    "XQ_ATTACHMENT_TOO_LARGE");
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var ms = new MemoryStream();
            var buffer = new byte[16384];
            int read;
            while ((read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
            {
                if (ms.Length + read > MaxAttachmentBytes)
                {
                    return AttachmentDownloadResult.Failed(
                        LearningReadFailureKind.InvalidResponse,
                        "XQ_ATTACHMENT_TOO_LARGE");
                }

                ms.Write(buffer, 0, read);
            }

            return AttachmentDownloadResult.Success(new DownloadedAttachmentImage(contentType, ms.ToArray()));
        }
    }
}

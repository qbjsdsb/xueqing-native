using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Xueqing.Windows.Core.Models;
using Xueqing.Windows.Core.Services;

namespace Xueqing.Windows.Infrastructure.Remote;

public sealed class PostgrestOrganizationInvitationCommand : IOrganizationInvitationCommand
{
    private const string RpcPath = "rest/v1/rpc/create_organization_invitation_v1";

    private readonly HttpClient _httpClient;
    private readonly Uri _rpcUri;
    private readonly string _apiKey;
    private readonly Func<CancellationToken, ValueTask<string?>> _accessTokenProvider;

    public PostgrestOrganizationInvitationCommand(
        HttpClient httpClient,
        Uri projectUri,
        string apiKey,
        Func<CancellationToken, ValueTask<string?>> accessTokenProvider)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _rpcUri = LearningReadProviderSupport.BuildRpcUri(
            projectUri ?? throw new ArgumentNullException(nameof(projectUri)),
            RpcPath);
        _apiKey = string.IsNullOrWhiteSpace(apiKey)
            ? throw new ArgumentException("Reference-provider API key is required.", nameof(apiKey))
            : apiKey;
        _accessTokenProvider = accessTokenProvider ?? throw new ArgumentNullException(nameof(accessTokenProvider));
    }

    public async Task<CreateOrganizationInvitationResult> ExecuteAsync(
        CreateOrganizationInvitationRequest request,
        Guid expectedActorAppUserId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var validation = Validate(request, expectedActorAppUserId);
        if (validation is not null)
        {
            return validation;
        }

        var accessToken = await _accessTokenProvider(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            return CreateOrganizationInvitationResult.Failed(
                CreateOrganizationInvitationFailureKind.AuthenticationRequired,
                "XQ_AUTH_REQUIRED");
        }

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, _rpcUri);
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        httpRequest.Headers.TryAddWithoutValidation("apikey", _apiKey);
        httpRequest.Content = JsonContent.Create(new
        {
            p_operation_id = request.OperationId,
            p_organization_id = request.OrganizationId,
            p_invited_email = NormalizeEmail(request.InvitedEmail),
            p_target_role = ToWireRole(request.TargetRole),
            p_target_can_teach = request.TargetCanTeach,
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
            return Unknown("XQ_RESULT_UNKNOWN_TIMEOUT");
        }
        catch (HttpRequestException)
        {
            return Unknown("XQ_RESULT_UNKNOWN_NETWORK");
        }

        using (response)
        {
            string body;
            try
            {
                body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return Unknown("XQ_RESULT_UNKNOWN_RESPONSE_TIMEOUT");
            }
            catch (HttpRequestException)
            {
                return Unknown("XQ_RESULT_UNKNOWN_RESPONSE_NETWORK");
            }

            if (response.IsSuccessStatusCode)
            {
                try
                {
                    return CreateOrganizationInvitationResult.Success(
                        ParseReceipt(body, request, expectedActorAppUserId));
                }
                catch (JsonException)
                {
                    return Unknown("XQ_RESULT_UNKNOWN_RECEIPT_JSON");
                }
                catch (InvalidDataException)
                {
                    return Unknown("XQ_RESULT_UNKNOWN_RECEIPT_CONTRACT");
                }
            }

            return MapFailure(response.StatusCode, body);
        }
    }

    private static CreateOrganizationInvitationResult? Validate(
        CreateOrganizationInvitationRequest request,
        Guid expectedActorAppUserId)
    {
        if (expectedActorAppUserId == Guid.Empty)
        {
            return CreateOrganizationInvitationResult.Failed(
                CreateOrganizationInvitationFailureKind.AuthenticationRequired,
                "XQ_CLIENT_ACTOR_REQUIRED");
        }
        if (request.OperationId == Guid.Empty)
        {
            return CreateOrganizationInvitationResult.Failed(
                CreateOrganizationInvitationFailureKind.Validation,
                "XQ_OPERATION_ID_REQUIRED");
        }
        if (request.OrganizationId == Guid.Empty)
        {
            return CreateOrganizationInvitationResult.Failed(
                CreateOrganizationInvitationFailureKind.Validation,
                "XQ_ORGANIZATION_REQUIRED");
        }

        var email = NormalizeEmail(request.InvitedEmail);
        if (email.Length is < 3 or > 320 ||
            email.Any(char.IsWhiteSpace) ||
            email.Count(ch => ch == '@') != 1 ||
            email.StartsWith('@') ||
            email.EndsWith('@'))
        {
            return CreateOrganizationInvitationResult.Failed(
                CreateOrganizationInvitationFailureKind.Validation,
                "XQ_INVITATION_EMAIL_INVALID");
        }

        return null;
    }

    private static CreateOrganizationInvitationReceipt ParseReceipt(
        string body,
        CreateOrganizationInvitationRequest request,
        Guid expectedActorAppUserId)
    {
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        LearningProjectionJson.RequireObject(root, "CreateOrganizationInvitation receipt");

        if (!string.Equals(
                LearningProjectionJson.GetRequiredString(root, "command"),
                "create_organization_invitation_v1",
                StringComparison.Ordinal))
        {
            throw new InvalidDataException("Unexpected Organization invitation receipt command.");
        }

        var operationId = LearningProjectionJson.GetRequiredGuid(root, "operation_id");
        var invitationId = LearningProjectionJson.GetRequiredGuid(root, "invitation_id");
        var actorAppUserId = LearningProjectionJson.GetRequiredGuid(root, "actor_app_user_id");
        var organizationId = LearningProjectionJson.GetRequiredGuid(root, "organization_id");
        var invitedEmail = LearningProjectionJson.GetRequiredNonBlankString(root, "invited_email");
        var targetRole = ParseWireRole(LearningProjectionJson.GetRequiredString(root, "target_role"));
        var targetCanTeach = GetRequiredBoolean(root, "target_can_teach");
        var status = LearningProjectionJson.GetRequiredString(root, "status");
        var expiresAt = LearningProjectionJson.GetRequiredTimestamp(root, "expires_at");
        var committedAt = LearningProjectionJson.GetRequiredTimestamp(root, "server_committed_at");

        if (operationId != request.OperationId ||
            actorAppUserId != expectedActorAppUserId ||
            organizationId != request.OrganizationId ||
            !string.Equals(invitedEmail, NormalizeEmail(request.InvitedEmail), StringComparison.Ordinal) ||
            targetRole != request.TargetRole ||
            targetCanTeach != request.TargetCanTeach)
        {
            throw new InvalidDataException("Organization invitation receipt does not match submitted intent.");
        }
        if (!string.Equals(status, "pending", StringComparison.Ordinal))
        {
            throw new InvalidDataException("New Organization invitation receipt must be pending.");
        }
        if (expiresAt <= committedAt ||
            expiresAt - committedAt != TimeSpan.FromDays(7))
        {
            throw new InvalidDataException("Organization invitation receipt expiry is invalid.");
        }

        return new CreateOrganizationInvitationReceipt(
            operationId,
            invitationId,
            actorAppUserId,
            organizationId,
            invitedEmail,
            targetRole,
            targetCanTeach,
            expiresAt,
            committedAt);
    }

    private static CreateOrganizationInvitationResult MapFailure(
        HttpStatusCode statusCode,
        string body)
    {
        if (statusCode == HttpStatusCode.Unauthorized)
        {
            return CreateOrganizationInvitationResult.Failed(
                CreateOrganizationInvitationFailureKind.AuthenticationRequired,
                "XQ_AUTH_REQUIRED");
        }

        var message = TryReadProviderMessage(body);
        if (message is "XQ_AUTH_REQUIRED" or "XQ_ACTOR_NOT_FOUND")
        {
            return CreateOrganizationInvitationResult.Failed(
                CreateOrganizationInvitationFailureKind.AuthenticationRequired,
                message);
        }
        if (message is "XQ_ACTOR_DISABLED" or "XQ_ORGANIZATION_MANAGEMENT_REQUIRED" or "XQ_INVITATION_ROLE_NOT_ALLOWED")
        {
            return CreateOrganizationInvitationResult.Failed(
                CreateOrganizationInvitationFailureKind.AuthorityChanged,
                message);
        }
        if (message == "XQ_OPERATION_REUSED_WITH_DIFFERENT_PAYLOAD")
        {
            return CreateOrganizationInvitationResult.Failed(
                CreateOrganizationInvitationFailureKind.OperationConflict,
                message);
        }
        if (message == "XQ_INVITATION_ALREADY_PENDING")
        {
            return CreateOrganizationInvitationResult.Failed(
                CreateOrganizationInvitationFailureKind.AlreadyPending,
                message);
        }
        if (message is "XQ_OPERATION_ID_REQUIRED" or "XQ_ORGANIZATION_REQUIRED" or
            "XQ_INVITATION_EMAIL_INVALID" or "XQ_INVITATION_ROLE_INVALID" or
            "XQ_INVITATION_TEACHING_CAPABILITY_REQUIRED")
        {
            return CreateOrganizationInvitationResult.Failed(
                CreateOrganizationInvitationFailureKind.Validation,
                message);
        }
        if ((int)statusCode is 408 or 425 or 429 || (int)statusCode >= 500)
        {
            return Unknown(message ?? $"XQ_RESULT_UNKNOWN_HTTP_{(int)statusCode}");
        }

        return CreateOrganizationInvitationResult.Failed(
            CreateOrganizationInvitationFailureKind.InvalidResponse,
            message ?? $"HTTP_{(int)statusCode}");
    }

    private static CreateOrganizationInvitationResult Unknown(string code) =>
        CreateOrganizationInvitationResult.Failed(
            CreateOrganizationInvitationFailureKind.ResultUnknown,
            code);

    private static string NormalizeEmail(string? value) =>
        value?.Trim().ToLowerInvariant() ?? string.Empty;

    private static string ToWireRole(OrganizationInvitationTargetRole role) => role switch
    {
        OrganizationInvitationTargetRole.Owner => "owner",
        OrganizationInvitationTargetRole.Admin => "admin",
        OrganizationInvitationTargetRole.Teacher => "teacher",
        _ => throw new ArgumentOutOfRangeException(nameof(role)),
    };

    private static OrganizationInvitationTargetRole ParseWireRole(string value) => value switch
    {
        "owner" => OrganizationInvitationTargetRole.Owner,
        "admin" => OrganizationInvitationTargetRole.Admin,
        "teacher" => OrganizationInvitationTargetRole.Teacher,
        _ => throw new InvalidDataException("Organization invitation receipt contains unknown target role."),
    };

    private static bool GetRequiredBoolean(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value) ||
            value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            throw new InvalidDataException($"Organization invitation receipt property '{name}' must be boolean.");
        }
        return value.GetBoolean();
    }

    private static string? TryReadProviderMessage(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind == JsonValueKind.Object &&
                document.RootElement.TryGetProperty("message", out var message) &&
                message.ValueKind == JsonValueKind.String)
            {
                return message.GetString();
            }
        }
        catch (JsonException)
        {
        }

        return null;
    }
}

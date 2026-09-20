using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Xueqing.Windows.Core.Models;
using Xueqing.Windows.Core.Services;

namespace Xueqing.Windows.Infrastructure.Remote;

public sealed class PostgrestAcceptOrganizationInvitationCommand : IAcceptOrganizationInvitationCommand
{
    private const string RpcPath = "rest/v1/rpc/accept_organization_invitation_v1";

    private readonly HttpClient _httpClient;
    private readonly Uri _rpcUri;
    private readonly string _apiKey;
    private readonly Func<CancellationToken, ValueTask<string?>> _accessTokenProvider;

    public PostgrestAcceptOrganizationInvitationCommand(
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

    public async Task<AcceptOrganizationInvitationResult> ExecuteAsync(
        AcceptOrganizationInvitationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var validation = Validate(request);
        if (validation is not null)
        {
            return validation;
        }

        var accessToken = await _accessTokenProvider(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            return AcceptOrganizationInvitationResult.Failed(
                AcceptOrganizationInvitationFailureKind.AuthenticationRequired,
                "XQ_AUTH_REQUIRED");
        }

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, _rpcUri);
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        httpRequest.Headers.TryAddWithoutValidation("apikey", _apiKey);
        httpRequest.Content = JsonContent.Create(new
        {
            p_operation_id = request.OperationId,
            p_invitation_id = request.InvitationId,
            p_display_name = request.DisplayName.Trim(),
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
                    return AcceptOrganizationInvitationResult.Success(
                        ParseReceipt(body, request));
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

    private static AcceptOrganizationInvitationResult? Validate(
        AcceptOrganizationInvitationRequest request)
    {
        if (request.OperationId == Guid.Empty)
        {
            return AcceptOrganizationInvitationResult.Failed(
                AcceptOrganizationInvitationFailureKind.Validation,
                "XQ_OPERATION_ID_REQUIRED");
        }
        if (request.InvitationId == Guid.Empty)
        {
            return AcceptOrganizationInvitationResult.Failed(
                AcceptOrganizationInvitationFailureKind.Validation,
                "XQ_INVITATION_REQUIRED");
        }

        var displayNameLength = request.DisplayName?.Trim().Length ?? 0;
        if (displayNameLength is < 1 or > 200)
        {
            return AcceptOrganizationInvitationResult.Failed(
                AcceptOrganizationInvitationFailureKind.Validation,
                "XQ_DISPLAY_NAME_INVALID");
        }

        return null;
    }

    private static AcceptOrganizationInvitationReceipt ParseReceipt(
        string body,
        AcceptOrganizationInvitationRequest request)
    {
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        LearningProjectionJson.RequireObject(root, "AcceptOrganizationInvitation receipt");

        if (!string.Equals(
                LearningProjectionJson.GetRequiredString(root, "command"),
                "accept_organization_invitation_v1",
                StringComparison.Ordinal))
        {
            throw new InvalidDataException("Unexpected Organization invitation acceptance receipt command.");
        }

        var operationId = LearningProjectionJson.GetRequiredGuid(root, "operation_id");
        var invitationId = LearningProjectionJson.GetRequiredGuid(root, "invitation_id");
        var actorAppUserId = LearningProjectionJson.GetRequiredGuid(root, "actor_app_user_id");
        var organizationId = LearningProjectionJson.GetRequiredGuid(root, "organization_id");
        var membershipRole = ParseMembershipRole(
            LearningProjectionJson.GetRequiredString(root, "membership_role"));
        var canTeach = GetRequiredBoolean(root, "can_teach");
        var status = LearningProjectionJson.GetRequiredString(root, "status");
        var committedAt = LearningProjectionJson.GetRequiredTimestamp(root, "server_committed_at");

        if (operationId != request.OperationId ||
            invitationId != request.InvitationId)
        {
            throw new InvalidDataException(
                "Organization invitation acceptance receipt does not match submitted intent.");
        }
        if (!string.Equals(status, "accepted", StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "Organization invitation acceptance receipt must be accepted.");
        }

        return new AcceptOrganizationInvitationReceipt(
            operationId,
            invitationId,
            actorAppUserId,
            organizationId,
            membershipRole,
            canTeach,
            committedAt);
    }

    private static AcceptOrganizationInvitationResult MapFailure(
        HttpStatusCode statusCode,
        string body)
    {
        if (statusCode == HttpStatusCode.Unauthorized)
        {
            return AcceptOrganizationInvitationResult.Failed(
                AcceptOrganizationInvitationFailureKind.AuthenticationRequired,
                "XQ_AUTH_REQUIRED");
        }

        var message = TryReadProviderMessage(body);
        if (message == "XQ_AUTH_REQUIRED")
        {
            return AcceptOrganizationInvitationResult.Failed(
                AcceptOrganizationInvitationFailureKind.AuthenticationRequired,
                message);
        }
        if (message is
            "XQ_INVITATION_EMAIL_REQUIRED" or
            "XQ_INVITATION_EMAIL_MISMATCH" or
            "XQ_INVITATION_NOT_FOUND" or
            "XQ_INVITATION_NOT_PENDING" or
            "XQ_INVITATION_EXPIRED")
        {
            return AcceptOrganizationInvitationResult.Failed(
                AcceptOrganizationInvitationFailureKind.InvitationUnavailable,
                message);
        }
        if (message is "XQ_IDENTITY_LINK_INACTIVE" or "XQ_ACTOR_DISABLED")
        {
            return AcceptOrganizationInvitationResult.Failed(
                AcceptOrganizationInvitationFailureKind.IdentityUnavailable,
                message);
        }
        if (message == "XQ_MEMBERSHIP_ALREADY_EXISTS")
        {
            return AcceptOrganizationInvitationResult.Failed(
                AcceptOrganizationInvitationFailureKind.MembershipConflict,
                message);
        }
        if (message == "XQ_OPERATION_REUSED_WITH_DIFFERENT_PAYLOAD")
        {
            return AcceptOrganizationInvitationResult.Failed(
                AcceptOrganizationInvitationFailureKind.OperationConflict,
                message);
        }
        if (message is
            "XQ_OPERATION_ID_REQUIRED" or
            "XQ_INVITATION_REQUIRED" or
            "XQ_DISPLAY_NAME_INVALID")
        {
            return AcceptOrganizationInvitationResult.Failed(
                AcceptOrganizationInvitationFailureKind.Validation,
                message);
        }
        if ((int)statusCode is 408 or 425 or 429 || (int)statusCode >= 500)
        {
            return Unknown(message ?? $"XQ_RESULT_UNKNOWN_HTTP_{(int)statusCode}");
        }

        return AcceptOrganizationInvitationResult.Failed(
            AcceptOrganizationInvitationFailureKind.InvalidResponse,
            message ?? $"HTTP_{(int)statusCode}");
    }

    private static AcceptOrganizationInvitationResult Unknown(string code) =>
        AcceptOrganizationInvitationResult.Failed(
            AcceptOrganizationInvitationFailureKind.ResultUnknown,
            code);

    private static OrganizationMembershipRole ParseMembershipRole(string value) => value switch
    {
        "owner" => OrganizationMembershipRole.Owner,
        "admin" => OrganizationMembershipRole.Admin,
        "teacher" => OrganizationMembershipRole.Teacher,
        _ => throw new InvalidDataException(
            "Organization invitation acceptance receipt contains unknown membership role."),
    };

    private static bool GetRequiredBoolean(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value) ||
            value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            throw new InvalidDataException(
                $"Organization invitation acceptance receipt property '{name}' must be boolean.");
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

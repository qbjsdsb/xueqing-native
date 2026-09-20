using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Xueqing.Windows.Core.Models;
using Xueqing.Windows.Core.Services;

namespace Xueqing.Windows.Infrastructure.Remote;

public sealed class SupabaseOrganizationInvitationDeliveryCommand :
    IOrganizationInvitationDeliveryCommand
{
    private const string FunctionPath =
        "functions/v1/organization-invitation-delivery";

    private readonly HttpClient _httpClient;
    private readonly Uri _functionUri;
    private readonly string _apiKey;
    private readonly Func<CancellationToken, ValueTask<string?>> _accessTokenProvider;

    public SupabaseOrganizationInvitationDeliveryCommand(
        HttpClient httpClient,
        Uri projectUri,
        string apiKey,
        Func<CancellationToken, ValueTask<string?>> accessTokenProvider)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        ArgumentNullException.ThrowIfNull(projectUri);
        if (!projectUri.IsAbsoluteUri)
        {
            throw new ArgumentException("Provider project URI must be absolute.", nameof(projectUri));
        }

        _functionUri = new Uri(
            projectUri.GetLeftPart(UriPartial.Authority).TrimEnd('/') + "/" + FunctionPath,
            UriKind.Absolute);
        _apiKey = string.IsNullOrWhiteSpace(apiKey)
            ? throw new ArgumentException("Reference-provider API key is required.", nameof(apiKey))
            : apiKey;
        _accessTokenProvider = accessTokenProvider ??
            throw new ArgumentNullException(nameof(accessTokenProvider));
    }

    public async Task<DeliverOrganizationInvitationResult> ExecuteAsync(
        DeliverOrganizationInvitationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.OperationId == Guid.Empty)
        {
            return Failed(
                DeliverOrganizationInvitationFailureKind.Validation,
                "XQ_OPERATION_ID_REQUIRED");
        }

        if (request.InvitationId == Guid.Empty)
        {
            return Failed(
                DeliverOrganizationInvitationFailureKind.Validation,
                "XQ_INVITATION_REQUIRED");
        }

        var accessToken = await _accessTokenProvider(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            return Failed(
                DeliverOrganizationInvitationFailureKind.AuthenticationRequired,
                "XQ_AUTH_REQUIRED");
        }

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, _functionUri);
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        httpRequest.Headers.TryAddWithoutValidation("apikey", _apiKey);
        httpRequest.Content = JsonContent.Create(new
        {
            operation_id = request.OperationId,
            invitation_id = request.InvitationId,
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
            return Unknown("XQ_INVITATION_DELIVERY_RESULT_UNKNOWN_TIMEOUT");
        }
        catch (HttpRequestException)
        {
            return Unknown("XQ_INVITATION_DELIVERY_RESULT_UNKNOWN_NETWORK");
        }

        using (response)
        {
            string body;
            try
            {
                body = await response.Content.ReadAsStringAsync(cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return Unknown("XQ_INVITATION_DELIVERY_RESULT_UNKNOWN_RESPONSE_TIMEOUT");
            }
            catch (HttpRequestException)
            {
                return Unknown("XQ_INVITATION_DELIVERY_RESULT_UNKNOWN_RESPONSE_NETWORK");
            }

            if (response.IsSuccessStatusCode)
            {
                try
                {
                    return DeliverOrganizationInvitationResult.Success(
                        ParseReceipt(body, request));
                }
                catch (JsonException)
                {
                    return Unknown("XQ_INVITATION_DELIVERY_RESULT_UNKNOWN_JSON");
                }
                catch (InvalidDataException)
                {
                    return Unknown("XQ_INVITATION_DELIVERY_RESULT_UNKNOWN_CONTRACT");
                }
            }

            return MapFailure(response.StatusCode, body);
        }
    }

    private static DeliverOrganizationInvitationReceipt ParseReceipt(
        string body,
        DeliverOrganizationInvitationRequest request)
    {
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        LearningProjectionJson.RequireObject(root, "Organization invitation delivery receipt");

        if (!GetRequiredBoolean(root, "ok"))
        {
            throw new InvalidDataException(
                "Successful Organization invitation delivery must report ok=true.");
        }

        var state = LearningProjectionJson.GetRequiredString(root, "state");
        var operationId = LearningProjectionJson.GetRequiredGuid(root, "operation_id");
        var deliveryId = LearningProjectionJson.GetRequiredGuid(root, "delivery_id");
        var invitationId = LearningProjectionJson.GetRequiredGuid(root, "invitation_id");

        if (!string.Equals(state, "sent", StringComparison.Ordinal) ||
            operationId != request.OperationId ||
            invitationId != request.InvitationId)
        {
            throw new InvalidDataException(
                "Organization invitation delivery receipt does not match submitted intent.");
        }

        return new DeliverOrganizationInvitationReceipt(
            operationId,
            deliveryId,
            invitationId);
    }

    private static DeliverOrganizationInvitationResult MapFailure(
        HttpStatusCode statusCode,
        string body)
    {
        if (statusCode == HttpStatusCode.Unauthorized)
        {
            return Failed(
                DeliverOrganizationInvitationFailureKind.AuthenticationRequired,
                "XQ_AUTH_REQUIRED");
        }

        var code = TryReadErrorCode(body);

        if (code is "XQ_AUTH_REQUIRED" or "XQ_ACTOR_NOT_FOUND")
        {
            return Failed(
                DeliverOrganizationInvitationFailureKind.AuthenticationRequired,
                code);
        }

        if (code is
            "XQ_ACTOR_DISABLED" or
            "XQ_ORGANIZATION_MANAGEMENT_REQUIRED" or
            "XQ_INVITATION_NOT_FOUND" or
            "XQ_INVITATION_NOT_PENDING" or
            "XQ_INVITATION_EXPIRED")
        {
            return Failed(
                DeliverOrganizationInvitationFailureKind.AuthorityChanged,
                code);
        }

        if (code == "XQ_OPERATION_REUSED_WITH_DIFFERENT_PAYLOAD")
        {
            return Failed(
                DeliverOrganizationInvitationFailureKind.OperationConflict,
                code);
        }

        if (code == "XQ_INVITATION_ALREADY_DELIVERED")
        {
            return Failed(
                DeliverOrganizationInvitationFailureKind.AlreadyDelivered,
                code);
        }

        if (code is
            "XQ_PROVIDER_DELIVERY_REJECTED" or
            "XQ_INVITATION_DELIVERY_RETRY_REQUIRES_RESEND")
        {
            return Failed(
                DeliverOrganizationInvitationFailureKind.ProviderRejected,
                code);
        }

        if (code is
            "XQ_INVITATION_DELIVERY_RESULT_UNKNOWN" or
            "XQ_INVITATION_DELIVERY_IN_PROGRESS")
        {
            return Unknown(code);
        }

        if (code is
            "XQ_OPERATION_ID_REQUIRED" or
            "XQ_INVITATION_REQUIRED" or
            "XQ_INVITATION_DELIVERY_INPUT_INVALID")
        {
            return Failed(
                DeliverOrganizationInvitationFailureKind.Validation,
                code);
        }

        if ((int)statusCode is 408 or 425 or 429 || (int)statusCode >= 500)
        {
            return Unknown(
                code ?? $"XQ_INVITATION_DELIVERY_RESULT_UNKNOWN_HTTP_{(int)statusCode}");
        }

        return Failed(
            DeliverOrganizationInvitationFailureKind.InvalidResponse,
            code ?? $"HTTP_{(int)statusCode}");
    }

    private static string? TryReadErrorCode(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            foreach (var propertyName in new[] { "error", "message" })
            {
                if (document.RootElement.TryGetProperty(propertyName, out var value) &&
                    value.ValueKind == JsonValueKind.String &&
                    !string.IsNullOrWhiteSpace(value.GetString()))
                {
                    return value.GetString()!.Trim();
                }
            }
        }
        catch (JsonException)
        {
        }

        return null;
    }

    private static bool GetRequiredBoolean(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value) ||
            value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            throw new InvalidDataException(
                $"Organization invitation delivery property '{name}' must be boolean.");
        }

        return value.GetBoolean();
    }

    private static DeliverOrganizationInvitationResult Unknown(string code) =>
        Failed(DeliverOrganizationInvitationFailureKind.ResultUnknown, code);

    private static DeliverOrganizationInvitationResult Failed(
        DeliverOrganizationInvitationFailureKind kind,
        string code) =>
        DeliverOrganizationInvitationResult.Failed(kind, code);
}

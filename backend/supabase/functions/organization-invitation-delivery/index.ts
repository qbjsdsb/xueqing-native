import { createClient } from "npm:@supabase/supabase-js@2.116.0";

type JsonObject = Record<string, unknown>;

const responseHeaders = {
  "Access-Control-Allow-Headers": "authorization, apikey, content-type, x-client-info",
  "Access-Control-Allow-Methods": "POST, OPTIONS",
  "Access-Control-Allow-Origin": "*",
  "Content-Type": "application/json; charset=utf-8",
};

class DeliveryError extends Error {
  readonly code: string;
  readonly status: number;

  constructor(code: string, status = 400) {
    super(code);
    this.name = "DeliveryError";
    this.code = code;
    this.status = status;
  }
}

function response(body: JsonObject, status = 200): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: responseHeaders,
  });
}

function requiredEnvironment(name: string): string {
  const value = Deno.env.get(name)?.trim();
  if (!value) {
    throw new DeliveryError("XQ_INVITATION_DELIVERY_UNAVAILABLE", 503);
  }
  return value;
}

function optionalEnvironment(...names: string[]): string | null {
  for (const name of names) {
    const value = Deno.env.get(name)?.trim();
    if (value) return value;
  }
  return null;
}

function mappedKey(environmentName: string): string | null {
  const raw = Deno.env.get(environmentName)?.trim();
  if (!raw) return null;

  try {
    const parsed = JSON.parse(raw) as JsonObject;
    const candidate = stringValue(parsed.default);
    if (!candidate) return null;

    // Current Supabase runtimes expose the concrete sb_* key in the JSON map.
    // Supporting an environment-variable indirection as well keeps local and
    // transitional self-hosted environments compatible without treating an
    // environment variable name as a credential.
    const indirect = Deno.env.get(candidate)?.trim();
    if (indirect) return indirect;
    if (candidate.startsWith("sb_") || candidate.split(".").length === 3) {
      return candidate;
    }
    return null;
  } catch {
    return null;
  }
}

function publishableKey(): string {
  const value = mappedKey("SUPABASE_PUBLISHABLE_KEYS") ??
    optionalEnvironment("SUPABASE_PUBLISHABLE_KEY", "SUPABASE_ANON_KEY");
  if (!value) {
    throw new DeliveryError("XQ_INVITATION_DELIVERY_UNAVAILABLE", 503);
  }
  return value;
}

function secretKey(): string {
  const value = mappedKey("SUPABASE_SECRET_KEYS") ??
    optionalEnvironment("SUPABASE_SECRET_KEY", "SUPABASE_SERVICE_ROLE_KEY");
  if (!value) {
    throw new DeliveryError("XQ_INVITATION_DELIVERY_UNAVAILABLE", 503);
  }
  return value;
}

function requiredBearer(request: Request): string {
  const header = request.headers.get("Authorization")?.trim() ?? "";
  if (!header.toLowerCase().startsWith("bearer ")) {
    throw new DeliveryError("XQ_AUTH_REQUIRED", 401);
  }
  const token = header.slice(7).trim();
  if (!token) throw new DeliveryError("XQ_AUTH_REQUIRED", 401);
  return token;
}

function asObject(value: unknown): JsonObject {
  if (!value || typeof value !== "object" || Array.isArray(value)) {
    throw new DeliveryError("XQ_INVITATION_DELIVERY_INVALID_RESPONSE", 502);
  }
  return value as JsonObject;
}

function stringValue(value: unknown): string | null {
  if (typeof value !== "string") return null;
  const normalized = value.trim();
  return normalized ? normalized : null;
}

function booleanValue(value: unknown): boolean | null {
  return typeof value === "boolean" ? value : null;
}

const uuidPattern =
  /^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i;

function requiredUuid(value: unknown): string {
  const normalized = stringValue(value);
  if (!normalized || !uuidPattern.test(normalized)) {
    throw new DeliveryError("XQ_INVITATION_DELIVERY_INPUT_INVALID");
  }
  return normalized;
}

function publicRpcError(error: { message?: string } | null | undefined): DeliveryError {
  const message = error?.message?.trim() ?? "";
  const firstLine = message.split("\n", 1)[0]?.trim() ?? "";
  if (firstLine.startsWith("XQ_") && firstLine.length <= 120) {
    const status = firstLine.includes("AUTH") ? 401
      : firstLine.includes("MANAGEMENT_REQUIRED") ? 403
      : firstLine.includes("IN_PROGRESS") || firstLine.includes("ALREADY") ? 409
      : 400;
    return new DeliveryError(firstLine, status);
  }
  return new DeliveryError("XQ_INVITATION_DELIVERY_PROVIDER_BOUNDARY_FAILED", 502);
}

function redirectForInvitation(supabaseUrl: string, invitationId: string): string {
  let base = optionalEnvironment("XUEQING_INVITATION_REDIRECT_URL");
  if (!base) {
    const provider = new URL(supabaseUrl);
    const isLocal = provider.hostname === "127.0.0.1" || provider.hostname === "localhost";
    if (!isLocal) {
      throw new DeliveryError("XQ_INVITATION_DELIVERY_REDIRECT_NOT_CONFIGURED", 503);
    }
    base = "http://127.0.0.1:3000/invitation";
  }

  let redirect: URL;
  try {
    redirect = new URL(base);
  } catch {
    throw new DeliveryError("XQ_INVITATION_DELIVERY_REDIRECT_NOT_CONFIGURED", 503);
  }
  redirect.searchParams.set("invitation_id", invitationId);
  return redirect.toString();
}

async function callServiceCompletion(
  serviceClient: ReturnType<typeof createClient>,
  functionName: string,
  params: JsonObject,
): Promise<JsonObject> {
  const { data, error } = await serviceClient.rpc(functionName, params);
  if (error) throw publicRpcError(error);
  return asObject(data);
}

async function handle(request: Request): Promise<Response> {
  if (request.method === "OPTIONS") return response({ ok: true });
  if (request.method !== "POST") {
    return response({ ok: false, error: "XQ_METHOD_NOT_ALLOWED" }, 405);
  }

  const accessToken = requiredBearer(request);
  const supabaseUrl = requiredEnvironment("SUPABASE_URL");
  const publishableApiKey = publishableKey();
  const serviceKey = secretKey();

  let input: JsonObject;
  try {
    input = asObject(await request.json());
  } catch (error) {
    if (error instanceof DeliveryError) throw error;
    throw new DeliveryError("XQ_INVITATION_DELIVERY_INPUT_INVALID");
  }

  const operationId = requiredUuid(input.operation_id);
  const invitationId = requiredUuid(input.invitation_id);

  const userClient = createClient(supabaseUrl, publishableApiKey, {
    auth: { autoRefreshToken: false, persistSession: false },
    global: { headers: { Authorization: `Bearer ${accessToken}` } },
  });

  const { data: claimData, error: claimError } = await userClient.rpc(
    "begin_organization_invitation_delivery_v1",
    {
      p_operation_id: operationId,
      p_invitation_id: invitationId,
    },
  );
  if (claimError) throw publicRpcError(claimError);

  const claim = asObject(claimData);
  const deliveryId = requiredUuid(claim.delivery_id);
  const state = stringValue(claim.state);
  const shouldDispatch = booleanValue(claim.should_dispatch);

  if (state === "sent" && shouldDispatch === false) {
    return response({
      ok: true,
      operation_id: operationId,
      delivery_id: deliveryId,
      invitation_id: invitationId,
      state: "sent",
    });
  }

  if (state === "failed" && shouldDispatch === false) {
    return response({
      ok: false,
      error: stringValue(claim.failure_code) ?? "XQ_INVITATION_DELIVERY_FAILED",
      operation_id: operationId,
      delivery_id: deliveryId,
      invitation_id: invitationId,
      state: "failed",
    }, 422);
  }

  if (state === "dispatching" && shouldDispatch === false) {
    return response({
      ok: false,
      error: "XQ_INVITATION_DELIVERY_RESULT_UNKNOWN",
      operation_id: operationId,
      delivery_id: deliveryId,
      invitation_id: invitationId,
      state: "dispatching",
    }, 409);
  }

  if (state !== "dispatching" || shouldDispatch !== true) {
    throw new DeliveryError("XQ_INVITATION_DELIVERY_INVALID_RESPONSE", 502);
  }

  const recipient = stringValue(claim.invited_email);
  if (!recipient) {
    throw new DeliveryError("XQ_INVITATION_DELIVERY_INVALID_RESPONSE", 502);
  }

  const redirectTo = redirectForInvitation(supabaseUrl, invitationId);
  const authClient = createClient(supabaseUrl, publishableApiKey, {
    auth: { autoRefreshToken: false, persistSession: false },
  });

  try {
    const { error: providerError } = await authClient.auth.signInWithOtp({
      email: recipient,
      options: {
        shouldCreateUser: true,
        emailRedirectTo: redirectTo,
      },
    });

    if (providerError) {
      const status = typeof providerError.status === "number"
        ? providerError.status
        : 0;
      const knownRejected =
        status >= 400 && status < 500 && ![408, 425, 429].includes(status);

      if (knownRejected) {
        const serviceClient = createClient(supabaseUrl, serviceKey, {
          auth: { autoRefreshToken: false, persistSession: false },
        });
        await callServiceCompletion(
          serviceClient,
          "fail_organization_invitation_delivery_v1",
          {
            p_operation_id: operationId,
            p_delivery_id: deliveryId,
            p_failure_code: "XQ_PROVIDER_DELIVERY_REJECTED",
          },
        );
        return response({
          ok: false,
          error: "XQ_PROVIDER_DELIVERY_REJECTED",
          operation_id: operationId,
          delivery_id: deliveryId,
          invitation_id: invitationId,
          state: "failed",
        }, 422);
      }

      return response({
        ok: false,
        error: "XQ_INVITATION_DELIVERY_RESULT_UNKNOWN",
        operation_id: operationId,
        delivery_id: deliveryId,
        invitation_id: invitationId,
        state: "dispatching",
      }, 503);
    }
  } catch {
    // A transport/runtime failure after dispatch was claimed is deliberately
    // result-unknown. Never auto-send again under the same or a new operation.
    return response({
      ok: false,
      error: "XQ_INVITATION_DELIVERY_RESULT_UNKNOWN",
      operation_id: operationId,
      delivery_id: deliveryId,
      invitation_id: invitationId,
      state: "dispatching",
    }, 503);
  }

  const serviceClient = createClient(supabaseUrl, serviceKey, {
    auth: { autoRefreshToken: false, persistSession: false },
  });

  try {
    await callServiceCompletion(
      serviceClient,
      "complete_organization_invitation_delivery_v1",
      {
        p_operation_id: operationId,
        p_delivery_id: deliveryId,
      },
    );
  } catch {
    // Email was accepted by the provider but completion is unknown. Preserve
    // dispatching so replay resolves instead of accidentally sending twice.
    return response({
      ok: false,
      error: "XQ_INVITATION_DELIVERY_RESULT_UNKNOWN",
      operation_id: operationId,
      delivery_id: deliveryId,
      invitation_id: invitationId,
      state: "dispatching",
    }, 503);
  }

  return response({
    ok: true,
    operation_id: operationId,
    delivery_id: deliveryId,
    invitation_id: invitationId,
    state: "sent",
  });
}

Deno.serve(async (request) => {
  try {
    return await handle(request);
  } catch (error) {
    if (error instanceof DeliveryError) {
      return response({ ok: false, error: error.code }, error.status);
    }
    return response(
      { ok: false, error: "XQ_INVITATION_DELIVERY_UNAVAILABLE" },
      503,
    );
  }
});

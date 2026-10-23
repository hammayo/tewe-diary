import type { TokenRequest, ValidationFailureResponse } from "./types";

// D14: the API version is one constant. Switching back to v1 is one line.
export const API_BASE = "/api/v2";
export const IDENTITY_BASE = "/identity";

export type ApiErrorKind = "validation" | "unauthorized" | "forbidden" | "notFound" | "server" | "network";

export class ApiError extends Error {
  constructor(
    readonly status: number,
    readonly kind: ApiErrorKind,
    message: string,
    /** Validation messages keyed by lower-cased property name ("title", "yearofrelease"). */
    readonly fieldErrors: Record<string, string> = {},
  ) {
    super(message);
    this.name = "ApiError";
  }
}

// D4: the token lives in module memory only. It is never written to storage, the URL or the console.
let token: string | null = null;
let unauthorizedHandler: () => void = () => {};

export function setToken(value: string | null): void {
  token = value;
}

export function onUnauthorized(handler: () => void): void {
  unauthorizedHandler = handler;
}

function kindFor(status: number): ApiErrorKind {
  if (status === 400) return "validation";
  if (status === 401) return "unauthorized";
  if (status === 403) return "forbidden";
  if (status === 404) return "notFound";
  return "server";
}

async function toApiError(response: Response): Promise<ApiError> {
  const kind = kindFor(response.status);
  const fieldErrors: Record<string, string> = {};
  let message = response.statusText || `Request failed (${response.status})`;

  if (kind === "validation") {
    const body = (await response.json().catch(() => null)) as ValidationFailureResponse | null;
    for (const { propertyName, message: text } of body?.errors ?? []) {
      fieldErrors[propertyName.toLowerCase()] ??= text;
    }
    message = Object.values(fieldErrors)[0] ?? message;
  }
  return new ApiError(response.status, kind, message, fieldErrors);
}

async function send(url: string, init: RequestInit): Promise<Response> {
  const headers = new Headers(init.headers);
  if (init.body !== undefined && !headers.has("Content-Type")) headers.set("Content-Type", "application/json");
  if (token) headers.set("Authorization", `Bearer ${token}`);

  let response: Response;
  try {
    // An absolute URL keeps this working outside a browser page (the tests) as well.
    response = await fetch(new URL(url, window.location.href), { ...init, headers });
  } catch {
    throw new ApiError(0, "network", "The API cannot be reached.");
  }
  if (!response.ok) {
    const error = await toApiError(response);
    if (error.kind === "unauthorized") unauthorizedHandler();
    throw error;
  }
  return response;
}

/** Calls `${API_BASE}${path}` and parses the JSON body (undefined for an empty body). */
export async function request<T>(path: string, init: RequestInit = {}): Promise<T> {
  const response = await send(`${API_BASE}${path}`, init);
  const text = await response.text();
  return (text ? JSON.parse(text) : undefined) as T; // writes may answer with an empty body
}

/** Identity.Api answers with the bare JWT string, sometimes JSON-quoted. */
export async function fetchToken(body: TokenRequest): Promise<string> {
  const response = await send(`${IDENTITY_BASE}/token`, { method: "POST", body: JSON.stringify(body) });
  return (await response.text()).trim().replace(/^"|"$/g, "");
}

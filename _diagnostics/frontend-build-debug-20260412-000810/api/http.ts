import { toApiError } from "./errors";
import { getAuthHeaders } from "./httpAuth";

const API_BASE_URL = (import.meta.env.VITE_API_BASE_URL ?? "").replace(/\/+$/, "");

function toUrl(path: string): string {
  if (/^https?:\/\//i.test(path)) {
    return path;
  }

  if (!path.startsWith("/")) {
    return `${API_BASE_URL}/${path}`;
  }

  return `${API_BASE_URL}${path}`;
}

function createHeaders(auth: boolean, hasBody: boolean): HeadersInit {
  return {
    ...(hasBody ? { "Content-Type": "application/json" } : {}),
    ...(auth ? getAuthHeaders() : {}),
  };
}

export async function apiGet<T>(url: string, auth = true): Promise<T> {
  const response = await fetch(toUrl(url), {
    method: "GET",
    headers: createHeaders(auth, false),
  });

  if (!response.ok) {
    throw await toApiError(response);
  }

  return (await response.json()) as T;
}

export async function apiPost<TResponse, TRequest = unknown>(
  url: string,
  body?: TRequest,
  auth = true,
): Promise<TResponse> {
  const response = await fetch(toUrl(url), {
    method: "POST",
    headers: createHeaders(auth, body !== undefined),
    body: body === undefined ? undefined : JSON.stringify(body),
  });

  if (!response.ok) {
    throw await toApiError(response);
  }

  return (await response.json()) as TResponse;
}
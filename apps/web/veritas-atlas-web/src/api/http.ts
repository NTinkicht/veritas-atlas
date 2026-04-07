import { toApiError } from "./errors";
import { getAuthHeaders } from "./httpAuth";

function createHeaders(auth: boolean, hasBody: boolean): HeadersInit {
  return {
    ...(hasBody ? { "Content-Type": "application/json" } : {}),
    ...(auth ? getAuthHeaders() : {}),
  };
}

export async function apiGet<T>(url: string, auth = true): Promise<T> {
  const response = await fetch(url, {
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
  const response = await fetch(url, {
    method: "POST",
    headers: createHeaders(auth, body !== undefined),
    body: body === undefined ? undefined : JSON.stringify(body),
  });

  if (!response.ok) {
    throw await toApiError(response);
  }

  return (await response.json()) as TResponse;
}
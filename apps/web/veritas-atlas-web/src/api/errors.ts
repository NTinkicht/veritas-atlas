import type { ApiError } from "./contracts";

export async function toApiError(response: Response): Promise<ApiError> {
  let raw: unknown = null;
  let message = `HTTP ${response.status}`;

  try {
    raw = await response.json();
    if (raw && typeof raw === "object") {
      const candidate =
        (raw as Record<string, unknown>).message ??
        (raw as Record<string, unknown>).reason ??
        (raw as Record<string, unknown>).title ??
        (raw as Record<string, unknown>).detail;

      if (typeof candidate === "string" && candidate.trim().length > 0) {
        message = candidate;
      }
    }
  } catch {
    try {
      const text = await response.text();
      if (text.trim().length > 0) {
        message = text;
      }
    } catch {
      // ignore secondary parse failures
    }
  }

  return {
    status: response.status,
    message,
    raw,
  };
}
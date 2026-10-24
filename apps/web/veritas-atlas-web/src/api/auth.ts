import type { AuthMeResponse, LoginRequest, LoginResponse } from "./contracts";
import { apiGet, apiPost, apiPostNoContent } from "./http";

export async function login(request: LoginRequest): Promise<LoginResponse> {
  return apiPost<LoginResponse, LoginRequest>("/api/v1/auth/login", request, false);
}

export async function getAuthMe(): Promise<AuthMeResponse> {
  return apiGet<AuthMeResponse>("/api/v1/auth/me", true);
}
export type { LoginRequest, LoginResponse, AuthMeResponse } from "./contracts";

export async function fetchCurrentUser(): Promise<AuthMeResponse> {
  return getAuthMe();
}

export function clearAuthState(): void {
  localStorage.removeItem("veritas_atlas_access_token");
}

/**
 * VER-P0-02 (#79): revoke the bearer server-side, then always drop it locally,
 * even if the server call fails (an already revoked or expired token).
 */
export async function logout(): Promise<void> {
  try {
    await apiPostNoContent("/api/v1/auth/logout", true);
  } finally {
    clearAuthState();
  }
}

export type CurrentUserResponse = AuthMeResponse;

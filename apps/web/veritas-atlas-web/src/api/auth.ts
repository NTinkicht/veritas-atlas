import type { AuthMeResponse, LoginRequest, LoginResponse } from "./contracts";
import { apiGet, apiPost } from "./http";

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

export type CurrentUserResponse = AuthMeResponse;

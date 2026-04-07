import { getAuthHeaders } from "./httpAuth";

export type PublicAuthDiagnostics = {
  mode: string;
  jwtIssuer: string;
  jwtAudience: string;
  timestampUtc: string;
};

export type ProtectedAuthDiagnostics = {
  mode: string;
  username: string;
  role: string;
  isAuthenticated: boolean;
  claims: Array<{ type: string; value: string }>;
  timestampUtc: string;
};

export async function getPublicAuthDiagnostics(): Promise<PublicAuthDiagnostics> {
  const response = await fetch("/api/v1/auth-diagnostics/public");

  if (!response.ok) {
    const text = await response.text();
    throw new Error(text || `HTTP ${response.status}`);
  }

  return response.json() as Promise<PublicAuthDiagnostics>;
}

export async function getProtectedAuthDiagnostics(): Promise<ProtectedAuthDiagnostics> {
  const response = await fetch("/api/v1/auth-diagnostics/protected", {
    headers: getAuthHeaders(),
  });

  if (!response.ok) {
    const text = await response.text();
    throw new Error(text || `HTTP ${response.status}`);
  }

  return response.json() as Promise<ProtectedAuthDiagnostics>;
}
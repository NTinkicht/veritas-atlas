const TOKEN_KEY = "veritas_atlas_access_token";

export function setAccessToken(token: string): void {
  localStorage.setItem(TOKEN_KEY, token);
}

export function getAccessToken(): string | null {
  return localStorage.getItem(TOKEN_KEY);
}

export function clearAccessToken(): void {
  localStorage.removeItem(TOKEN_KEY);
}

export function hasAccessToken(): boolean {
  return !!getAccessToken();
}

export function getAuthHeaders(): Record<string, string> {
  const token = getAccessToken();
  return token ? { Authorization: `Bearer ${token}` } : {};
}
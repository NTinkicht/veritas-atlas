export type LoginRequest = {
  username: string;
  password: string;
};

export type LoginResponse = {
  accessToken: string;
  tokenType: string;
  expiresAtUtc: string;
  username: string;
  role: string;
};

export type CurrentUserResponse = {
  username: string;
  role: string;
  isAuthenticated: boolean;
  timestampUtc: string;
};

const TOKEN_KEY = "veritas-auth-token";
const USER_KEY = "veritas-auth-user";

export function getAccessToken(): string | null {
  if (typeof window === "undefined") return null;
  return window.localStorage.getItem(TOKEN_KEY);
}

export function setAccessToken(token: string) {
  if (typeof window === "undefined") return;
  window.localStorage.setItem(TOKEN_KEY, token);
}

export function clearAuthState() {
  if (typeof window === "undefined") return;
  window.localStorage.removeItem(TOKEN_KEY);
  window.localStorage.removeItem(USER_KEY);
}

export async function login(request: LoginRequest): Promise<LoginResponse> {
  const response = await fetch("/api/v1/auth/login", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(request),
  });

  if (!response.ok) {
    const text = await response.text();
    throw new Error(text || `HTTP ${response.status}`);
  }

  const data = (await response.json()) as LoginResponse;
  setAccessToken(data.accessToken);
  return data;
}

export async function fetchCurrentUser(): Promise<CurrentUserResponse> {
  const token = getAccessToken();

  const response = await fetch("/api/v1/auth/me", {
    headers: token ? { Authorization: `Bearer ${token}` } : {},
  });

  if (!response.ok) {
    const text = await response.text();
    throw new Error(text || `HTTP ${response.status}`);
  }

  const data = (await response.json()) as CurrentUserResponse;

  if (typeof window !== "undefined") {
    window.localStorage.setItem(USER_KEY, JSON.stringify(data));
  }

  return data;
}
const ROLE_KEY = "veritas-workflow-role";

export function getWorkflowRole(): string {
  if (typeof window === "undefined") {
    return "operator";
  }

  return window.localStorage.getItem(ROLE_KEY) ?? "operator";
}

export function setWorkflowRole(role: string) {
  if (typeof window === "undefined") {
    return;
  }

  window.localStorage.setItem(ROLE_KEY, role);
}

export function getWorkflowRoleHeaders(): HeadersInit {
  return {
    "X-Role": getWorkflowRole(),
  };
}
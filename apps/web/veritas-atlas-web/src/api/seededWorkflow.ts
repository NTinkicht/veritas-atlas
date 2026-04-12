import { getAuthHeaders } from "./httpAuth";

const API_BASE_URL = (import.meta.env.VITE_API_BASE_URL ?? "").replace(/\/+$/, "");

function buildUrl(path: string): string {
  return path.startsWith("http") ? path : `${API_BASE_URL}${path}`;
}

async function apiPost<T>(paths: string[]): Promise<T> {
  let lastMessage = "No endpoint candidates were provided.";

  for (const path of paths) {
    const response = await fetch(buildUrl(path), {
      method: "POST",
      headers: {
        ...getAuthHeaders(),
      },
    });

    const text = await response.text();
    let payload: unknown = undefined;

    if (text) {
      try {
        payload = JSON.parse(text);
      } catch {
        payload = { message: text };
      }
    }

    if (response.ok) {
      return (payload ?? {}) as T;
    }

    const message =
      payload && typeof payload === "object" && "message" in payload
        ? String((payload as { message?: unknown }).message ?? `HTTP ${response.status}`)
        : `HTTP ${response.status}`;

    if (response.status === 404) {
      lastMessage = message;
      continue;
    }

    throw new Error(message);
  }

  throw new Error(lastMessage);
}

export type SeedLifecycleResponse = {
  caseId: string;
  claimAId: string;
  claimBId: string;
  contradictionId: string;
  caseStatus: string;
  contradictionStatus: string;
};

export type StandardActionResponse = {
  message?: string;
  status?: string;
};

function normalizeSeedPayload(input: unknown): SeedLifecycleResponse {
  const source = (input ?? {}) as Record<string, unknown>;

  const claimAId =
    source.claimAId ??
    source.claimAid ??
    source.claim_a_id ??
    source.claimA ??
    source.claim1Id ??
    source.leftClaimId ??
    "";

  const claimBId =
    source.claimBId ??
    source.claimBid ??
    source.claim_b_id ??
    source.claimB ??
    source.claim2Id ??
    source.rightClaimId ??
    "";

  return {
    caseId: String(source.caseId ?? source.caseID ?? ""),
    claimAId: String(claimAId ?? ""),
    claimBId: String(claimBId ?? ""),
    contradictionId: String(source.contradictionId ?? source.contradictionID ?? ""),
    caseStatus: String(source.caseStatus ?? ""),
    contradictionStatus: String(source.contradictionStatus ?? ""),
  };
}

export async function seedLifecycle(): Promise<SeedLifecycleResponse> {
  const result = await apiPost<unknown>([
    "/api/v1/actions/seed/lifecycle",
    "/api/v1/workflow/seed/lifecycle",
  ]);

  return normalizeSeedPayload(result);
}

export async function preparePublication(caseId: string): Promise<StandardActionResponse> {
  return apiPost<StandardActionResponse>([
    `/api/v1/publication-workflow/cases/${caseId}/prepare`,
    `/api/v1/actions/publication-workflow/cases/${caseId}/prepare`,
  ]);
}

export async function resolveContradiction(contradictionId: string): Promise<StandardActionResponse> {
  return apiPost<StandardActionResponse>([
    `/api/v1/actions/contradictions/${contradictionId}/resolve`,
    `/api/v1/workflow/contradictions/${contradictionId}/resolve`,
  ]);
}
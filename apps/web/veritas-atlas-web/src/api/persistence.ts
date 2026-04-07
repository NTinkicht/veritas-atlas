import { getAuthHeaders } from "./httpAuth";

export type ScenarioSnapshotResponse = {
  exists: boolean;
  caseId?: string | null;
  claimAId?: string | null;
  claimBId?: string | null;
  contradictionId?: string | null;
  createdAtUtc?: string | null;
  caseStatus?: string | null;
  contradictionStatus?: string | null;
  timestampUtc: string;
};

export type ResetResponse = {
  success: boolean;
  message: string;
  timestampUtc: string;
};

export async function getScenarioSnapshot(): Promise<ScenarioSnapshotResponse> {
  const response = await fetch("/api/v1/persistence/snapshot", {
    headers: getAuthHeaders(),
  });

  if (!response.ok) {
    const text = await response.text();
    throw new Error(text || `HTTP ${response.status}`);
  }

  return response.json() as Promise<ScenarioSnapshotResponse>;
}

export async function resetScenarioState(): Promise<ResetResponse> {
  const response = await fetch("/api/v1/persistence/reset", {
    method: "POST",
    headers: getAuthHeaders(),
  });

  if (!response.ok) {
    const text = await response.text();
    throw new Error(text || `HTTP ${response.status}`);
  }

  return response.json() as Promise<ResetResponse>;
}
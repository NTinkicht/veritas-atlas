import { apiGet, apiPost } from "./http";
import type { ResetResponse, ScenarioSnapshotResponse } from "./contracts";

export async function getScenarioSnapshot(): Promise<ScenarioSnapshotResponse> {
  return apiGet<ScenarioSnapshotResponse>("/api/v1/persistence/snapshot", true);
}

export async function resetScenarioState(): Promise<ResetResponse> {
  return apiPost<ResetResponse>("/api/v1/persistence/reset", undefined, true);
}
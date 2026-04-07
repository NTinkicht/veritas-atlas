import type { ResetResponse, ScenarioSnapshotResponse } from "./contracts";
import { apiGet, apiPost } from "./http";

export async function getScenarioSnapshot(): Promise<ScenarioSnapshotResponse> {
  return apiGet<ScenarioSnapshotResponse>("/api/v1/persistence/snapshot");
}

export async function resetScenarioState(): Promise<ResetResponse> {
  return apiPost<ResetResponse>("/api/v1/persistence/reset");
}
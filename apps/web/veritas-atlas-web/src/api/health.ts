import { apiGet } from "./client";

export type HealthResponse = string | Record<string, unknown>;

export function getHealth(): Promise<HealthResponse> {
  return apiGet<HealthResponse>("/health");
}

export function getDatabaseHealth(): Promise<HealthResponse> {
  return apiGet<HealthResponse>("/health/db");
}

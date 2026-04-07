import { getAuthHeaders } from "./httpAuth";

export type WorkflowSeedResponse = {
  caseId: string;
  primaryClaimId: string;
  secondaryClaimId: string;
  contradictionId: string;
  caseStatus: string;
  contradictionStatus: string;
  timestampUtc: string;
};

export async function seedWorkflowLifecycle(): Promise<WorkflowSeedResponse> {
  const response = await fetch("/api/v1/actions/seed/lifecycle", {
    method: "POST",
    headers: getAuthHeaders(),
  });

  if (!response.ok) {
    const text = await response.text();
    throw new Error(text || `HTTP ${response.status}`);
  }

  return response.json() as Promise<WorkflowSeedResponse>;
}
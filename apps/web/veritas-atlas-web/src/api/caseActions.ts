import { API_BASE_URL } from "./client";

type CaseActionRequest = {
  approvedBy?: string;
  rejectedBy?: string;
  notes?: string;
};

async function postCaseAction(path: string, body: CaseActionRequest): Promise<void> {
  const response = await fetch(`${API_BASE_URL}${path}`, {
    method: "POST",
    headers: {
      "Content-Type": "application/json",
    },
    body: JSON.stringify(body),
  });

  if (!response.ok) {
    throw new Error(`POST ${path} failed with status ${response.status}`);
  }
}

export async function approveCase(id: string, approvedBy = "frontend-user", notes = "Approved from frontend"): Promise<void> {
  return postCaseAction(`/api/v1/cases/${id}/approve`, {
    approvedBy,
    notes,
  });
}

export async function rejectCase(id: string, rejectedBy = "frontend-user", notes = "Rejected from frontend"): Promise<void> {
  return postCaseAction(`/api/v1/cases/${id}/reject`, {
    rejectedBy,
    notes,
  });
}

export type WorkflowRulesPayload = {
  transitions: Record<string, Record<string, string[]>>;
  roles: Record<string, string[]>;
  timestampUtc: string;
};

export async function getWorkflowRules(): Promise<WorkflowRulesPayload> {
  const response = await fetch("/api/v1/workflow-audit/rules");

  if (!response.ok) {
    const text = await response.text();
    throw new Error(text || `HTTP ${response.status}`);
  }

  return response.json() as Promise<WorkflowRulesPayload>;
}
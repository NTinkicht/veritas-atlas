export async function getWorkflowRules(): Promise<string[]> {
  const response = await fetch("/api/v1/workflow-validation/rules");

  if (!response.ok) {
    const text = await response.text();
    throw new Error(text || `HTTP ${response.status}`);
  }

  return response.json() as Promise<string[]>;
}
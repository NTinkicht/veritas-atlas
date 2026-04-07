import { useQuery } from "@tanstack/react-query";
import { getWorkflowRules } from "../api/workflowValidation";

export function useWorkflowValidation() {
  return useQuery({
    queryKey: ["workflow-validation-rules"],
    queryFn: () => getWorkflowRules(),
  });
}
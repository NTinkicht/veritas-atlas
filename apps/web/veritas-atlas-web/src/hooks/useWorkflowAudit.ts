import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { clearWorkflowAudit, getWorkflowAuditEntries } from "../api/workflowAudit";

export function useWorkflowAuditEntries() {
  return useQuery({
    queryKey: ["workflow-audit-entries"],
    queryFn: () => getWorkflowAuditEntries(),
  });
}

export function useClearWorkflowAudit() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: () => clearWorkflowAudit(),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["workflow-audit-entries"] });
    },
  });
}
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { seedWorkflowLifecycle } from "../api/workflowSeed";

export function useWorkflowSeed() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: () => seedWorkflowLifecycle(),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["case-explorer"] });
      queryClient.invalidateQueries({ queryKey: ["cases"] });
      queryClient.invalidateQueries({ queryKey: ["claims"] });
      queryClient.invalidateQueries({ queryKey: ["contradictions"] });
    },
  });
}
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { getScenarioSnapshot, resetScenarioState } from "../api/persistence";

export function useScenarioSnapshot() {
  return useQuery({
    queryKey: ["scenario-snapshot"],
    queryFn: () => getScenarioSnapshot(),
  });
}

export function useResetScenarioState() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: () => resetScenarioState(),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ["scenario-snapshot"] });
      await queryClient.invalidateQueries({ queryKey: ["workflow-audit-entries"] });
      await queryClient.invalidateQueries({ queryKey: ["case-explorer"] });
      await queryClient.invalidateQueries({ queryKey: ["cases"] });
      await queryClient.invalidateQueries({ queryKey: ["claims"] });
      await queryClient.invalidateQueries({ queryKey: ["contradictions"] });
    },
  });
}
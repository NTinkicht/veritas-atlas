import { useMutation, useQueryClient } from "@tanstack/react-query";
import { approveCase, rejectCase } from "../api/caseActions";

export function useApproveCase() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (id: string) => approveCase(id),
    onSuccess: (_, id) => {
      queryClient.invalidateQueries({ queryKey: ["case-detail", id] });
      queryClient.invalidateQueries({ queryKey: ["cases"] });
      queryClient.invalidateQueries({ queryKey: ["dashboard"] });
    },
  });
}

export function useRejectCase() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (id: string) => rejectCase(id),
    onSuccess: (_, id) => {
      queryClient.invalidateQueries({ queryKey: ["case-detail", id] });
      queryClient.invalidateQueries({ queryKey: ["cases"] });
      queryClient.invalidateQueries({ queryKey: ["dashboard"] });
    },
  });
}

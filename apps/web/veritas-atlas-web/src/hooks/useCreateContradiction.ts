import { useMutation, useQueryClient } from "@tanstack/react-query";
import { createContradiction, type CreateContradictionRequest } from "../api/createContradiction";

export function useCreateContradiction() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (request: CreateContradictionRequest) => createContradiction(request),
    onSuccess: (_, request) => {
      queryClient.invalidateQueries({ queryKey: ["case-detail", request.caseId] });
      queryClient.invalidateQueries({ queryKey: ["cases"] });
      queryClient.invalidateQueries({ queryKey: ["dashboard"] });
    },
  });
}

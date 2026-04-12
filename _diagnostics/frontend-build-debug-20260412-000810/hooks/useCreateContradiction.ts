import { useMutation, useQueryClient } from "@tanstack/react-query";
import { createContradiction, type CreateContradictionRequest } from "../api/contradictions";

export function useCreateContradiction() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (request: CreateContradictionRequest) => createContradiction(request),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["contradictions"] });
    },
  });
}
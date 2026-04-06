import { useMutation, useQueryClient } from "@tanstack/react-query";
import { createEvidence, type CreateEvidenceRequest } from "../api/createEvidence";

export function useCreateEvidence() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (request: CreateEvidenceRequest) => createEvidence(request),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["dashboard"] });
    },
  });
}

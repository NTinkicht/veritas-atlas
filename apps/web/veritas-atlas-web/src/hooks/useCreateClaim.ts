import { useMutation, useQueryClient } from "@tanstack/react-query";
import { createClaim, type CreateClaimRequest } from "../api/createClaim";

export function useCreateClaim() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (request: CreateClaimRequest) => createClaim(request),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["cases"] });
      queryClient.invalidateQueries({ queryKey: ["dashboard"] });
    },
  });
}

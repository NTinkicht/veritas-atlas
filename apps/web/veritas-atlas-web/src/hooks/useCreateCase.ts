import { useMutation, useQueryClient } from "@tanstack/react-query";
import { createCase, type CreateCaseRequest } from "../api/createCase";

export function useCreateCase() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (request: CreateCaseRequest) => createCase(request),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["cases"] });
      queryClient.invalidateQueries({ queryKey: ["dashboard"] });
    },
  });
}

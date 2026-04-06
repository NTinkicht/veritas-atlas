import { useMutation, useQueryClient } from "@tanstack/react-query";
import { createSource, type CreateSourceRequest } from "../api/createSource";

export function useCreateSource() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (request: CreateSourceRequest) => createSource(request),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["dashboard"] });
    },
  });
}

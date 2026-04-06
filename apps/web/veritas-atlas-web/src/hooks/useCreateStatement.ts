import { useMutation, useQueryClient } from "@tanstack/react-query";
import { createStatement, type CreateStatementRequest } from "../api/createStatement";

export function useCreateStatement() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (request: CreateStatementRequest) => createStatement(request),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["dashboard"] });
    },
  });
}

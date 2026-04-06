import { useMutation, useQueryClient } from "@tanstack/react-query";
import { createPerson, type CreatePersonRequest } from "../api/createPerson";

export function useCreatePerson() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (request: CreatePersonRequest) => createPerson(request),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["persons"] });
      queryClient.invalidateQueries({ queryKey: ["dashboard"] });
    },
  });
}

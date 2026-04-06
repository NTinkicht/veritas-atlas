import { useMutation, useQueryClient } from "@tanstack/react-query";
import { addAliasToPerson } from "../api/personDetail";

type AddAliasInput = {
  personId: string;
  alias: string;
};

export function useAddAlias() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: ({ personId, alias }: AddAliasInput) =>
      addAliasToPerson(personId, {
        alias,
        createdBy: "frontend-user",
      }),
    onSuccess: (_, variables) => {
      queryClient.invalidateQueries({ queryKey: ["person-detail", variables.personId] });
      queryClient.invalidateQueries({ queryKey: ["persons"] });
    },
  });
}

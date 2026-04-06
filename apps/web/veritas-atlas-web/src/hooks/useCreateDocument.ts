import { useMutation, useQueryClient } from "@tanstack/react-query";
import { createDocument, type CreateDocumentRequest } from "../api/createDocument";

export function useCreateDocument() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (request: CreateDocumentRequest) => createDocument(request),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["dashboard"] });
    },
  });
}

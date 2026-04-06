import { useQuery } from "@tanstack/react-query";
import { getDocumentById } from "../api/documents";

export function useDocumentDetail(id?: string) {
  return useQuery({
    queryKey: ["document-detail", id],
    queryFn: () => getDocumentById(id!),
    enabled: !!id,
  });
}

import { useQuery } from "@tanstack/react-query";
import { getDocuments, type DocumentsQuery } from "../api/documents";

export function useDocuments(query?: DocumentsQuery) {
  return useQuery({
    queryKey: ["documents", query ?? {}],
    queryFn: () => getDocuments(query),
  });
}

import { useQuery } from "@tanstack/react-query";
import { getDocuments } from "../api/documents";

export function useDocuments() {
  return useQuery({
    queryKey: ["documents"],
    queryFn: getDocuments,
  });
}

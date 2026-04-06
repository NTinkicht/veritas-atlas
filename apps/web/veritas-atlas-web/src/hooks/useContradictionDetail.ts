import { useQuery } from "@tanstack/react-query";
import { getContradictionById } from "../api/contradictions";

export function useContradictionDetail(id?: string) {
  return useQuery({
    queryKey: ["contradiction-detail", id],
    queryFn: () => getContradictionById(id!),
    enabled: !!id,
  });
}
import { useQuery } from "@tanstack/react-query";
import { getCaseById } from "../api/caseDetail";

export function useCaseDetail(id: string | undefined) {
  return useQuery({
    queryKey: ["case-detail", id],
    queryFn: () => getCaseById(id!),
    enabled: !!id,
  });
}

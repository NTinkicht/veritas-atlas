import { useQuery } from "@tanstack/react-query";
import { getStatementById } from "../api/statements";

export function useStatementDetail(id?: string) {
  return useQuery({
    queryKey: ["statement-detail", id],
    queryFn: () => getStatementById(id!),
    enabled: !!id,
  });
}

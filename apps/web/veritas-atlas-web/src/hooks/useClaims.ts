import { useQuery } from "@tanstack/react-query";
import { getClaims } from "../api/claims";

export function useClaims(statementId?: string) {
  return useQuery({
    queryKey: ["claims", statementId ?? "all"],
    queryFn: () => getClaims(1, 20),
  });
}
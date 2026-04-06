import { useQuery } from "@tanstack/react-query";
import { getContradictions } from "../api/contradictions";

export function useContradictions(claimId?: string, caseId?: string) {
  return useQuery({
    queryKey: ["contradictions", claimId ?? "", caseId ?? ""],
    queryFn: () => getContradictions(claimId, caseId),
  });
}
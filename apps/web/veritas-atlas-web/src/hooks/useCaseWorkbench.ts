import { useMemo } from "react";
import { useCaseDetail } from "./useCaseDetail";
import { useClaims } from "./useClaims";
import { useContradictions } from "./useContradictions";

export function useCaseWorkbench(caseId?: string) {
  const caseQuery = useCaseDetail(caseId);
  const claimsQuery = useClaims();
  const contradictionsQuery = useContradictions(undefined, caseId);

  const linkedClaims = useMemo(() => {
    const items = claimsQuery.data?.items ?? [];
    if (!caseId) return [];
    return items.filter((x) => x.caseId === caseId);
  }, [claimsQuery.data, caseId]);

  const contradictionItems = contradictionsQuery.data?.items ?? [];

  return {
    caseQuery,
    claimsQuery,
    contradictionsQuery,
    linkedClaims,
    contradictionItems,
  };
}
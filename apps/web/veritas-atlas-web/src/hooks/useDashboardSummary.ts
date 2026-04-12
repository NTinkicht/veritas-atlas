import { useMemo } from "react";
import { useCases } from "./useCases";
import { useClaims } from "./useClaims";
import { useContradictions } from "./useContradictions";

export function useDashboardSummary() {
  const casesQuery = useCases();
  const claimsQuery = useClaims();
  const contradictionsQuery = useContradictions();

  return useMemo(() => {
    const caseCount = casesQuery.data?.totalCount ?? 0;
    const claimCount = claimsQuery.data?.totalCount ?? 0;
    const contradictionCount = contradictionsQuery.data?.totalCount ?? 0;

    return {
      caseCount,
      claimCount,
      contradictionCount,
      snapshotExists: "Unknown",
      isLoading:
        casesQuery.isLoading ||
        claimsQuery.isLoading ||
        contradictionsQuery.isLoading,
    };
  }, [
    casesQuery.data,
    claimsQuery.data,
    contradictionsQuery.data,
    casesQuery.isLoading,
    claimsQuery.isLoading,
    contradictionsQuery.isLoading,
  ]);
}
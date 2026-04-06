import { useQuery } from "@tanstack/react-query";
import { getCaseExplorer } from "../api/caseExplorer";

export function useCaseExplorer(page = 1, pageSize = 50) {
  return useQuery({
    queryKey: ["case-explorer", page, pageSize],
    queryFn: () => getCaseExplorer(page, pageSize),
  });
}
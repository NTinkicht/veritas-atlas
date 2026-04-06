import { useQuery } from "@tanstack/react-query";
import { getEvidenceList, type EvidenceQuery } from "../api/evidenceList";

export function useEvidenceList(query?: EvidenceQuery) {
  return useQuery({
    queryKey: ["evidence", query ?? {}],
    queryFn: () => getEvidenceList(query),
  });
}

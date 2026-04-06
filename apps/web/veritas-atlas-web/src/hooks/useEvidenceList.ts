import { useQuery } from "@tanstack/react-query";
import { getEvidenceList } from "../api/evidenceList";

export function useEvidenceList() {
  return useQuery({
    queryKey: ["evidence"],
    queryFn: getEvidenceList,
  });
}

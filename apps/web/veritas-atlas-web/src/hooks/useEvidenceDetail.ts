import { useQuery } from "@tanstack/react-query";
import { getEvidenceById } from "../api/evidenceList";

export function useEvidenceDetail(id?: string) {
  return useQuery({
    queryKey: ["evidence-detail", id],
    queryFn: () => getEvidenceById(id!),
    enabled: !!id,
  });
}

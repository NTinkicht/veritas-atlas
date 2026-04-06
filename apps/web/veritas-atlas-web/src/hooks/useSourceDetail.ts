import { useQuery } from "@tanstack/react-query";
import { getSourceById } from "../api/sources";

export function useSourceDetail(id?: string) {
  return useQuery({
    queryKey: ["source-detail", id],
    queryFn: () => getSourceById(id!),
    enabled: !!id,
  });
}

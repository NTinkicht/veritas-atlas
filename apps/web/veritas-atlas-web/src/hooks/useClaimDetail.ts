import { useQuery } from "@tanstack/react-query";
import { getClaimById } from "../api/claims";

export function useClaimDetail(id?: string) {
  return useQuery({
    queryKey: ["claim-detail", id],
    queryFn: () => getClaimById(id!),
    enabled: !!id,
  });
}

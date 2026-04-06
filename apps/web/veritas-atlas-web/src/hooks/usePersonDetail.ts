import { useQuery } from "@tanstack/react-query";
import { getPersonById } from "../api/personDetail";

export function usePersonDetail(id: string | undefined) {
  return useQuery({
    queryKey: ["person-detail", id],
    queryFn: () => getPersonById(id!),
    enabled: !!id,
  });
}

import { useQuery } from "@tanstack/react-query";
import { getCases } from "../api/cases";

export function useCases() {
  return useQuery({
    queryKey: ["cases"],
    queryFn: () => getCases(),
  });
}

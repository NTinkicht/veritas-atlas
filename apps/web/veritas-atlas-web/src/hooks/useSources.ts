import { useQuery } from "@tanstack/react-query";
import { getSources, type SourcesQuery } from "../api/sources";

export function useSources(query?: SourcesQuery) {
  return useQuery({
    queryKey: ["sources", query ?? {}],
    queryFn: () => getSources(query),
  });
}

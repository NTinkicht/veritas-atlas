import { useQuery } from "@tanstack/react-query";
import { getStatements } from "../api/statements";

export function useStatements() {
  return useQuery({
    queryKey: ["statements"],
    queryFn: getStatements,
  });
}

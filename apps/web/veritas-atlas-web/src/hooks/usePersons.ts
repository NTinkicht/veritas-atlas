import { useQuery } from "@tanstack/react-query";
import { getPersons } from "../api/persons";

export function usePersons() {
  return useQuery({
    queryKey: ["persons"],
    queryFn: getPersons,
  });
}

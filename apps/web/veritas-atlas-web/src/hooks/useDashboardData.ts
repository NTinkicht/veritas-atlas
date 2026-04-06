import { useQuery } from "@tanstack/react-query";
import { getDashboardData } from "../api/dashboard";

export function useDashboardData() {
  return useQuery({
    queryKey: ["dashboard"],
    queryFn: getDashboardData,
  });
}

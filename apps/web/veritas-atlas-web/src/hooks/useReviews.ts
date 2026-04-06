import { useQuery } from "@tanstack/react-query";
import { getReviews } from "../api/reviews";

export function useReviews() {
  return useQuery({
    queryKey: ["reviews"],
    queryFn: getReviews,
  });
}

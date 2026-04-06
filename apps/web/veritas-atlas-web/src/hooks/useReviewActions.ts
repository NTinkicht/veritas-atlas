import { useMutation, useQueryClient } from "@tanstack/react-query";
import { approveReview, rejectReview, requestReviewChanges } from "../api/reviewActions";

function invalidateAll(queryClient: ReturnType<typeof useQueryClient>) {
  queryClient.invalidateQueries({ queryKey: ["reviews"] });
  queryClient.invalidateQueries({ queryKey: ["cases"] });
  queryClient.invalidateQueries({ queryKey: ["dashboard"] });
}

export function useApproveReview() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (id: string) => approveReview(id),
    onSuccess: () => {
      invalidateAll(queryClient);
    },
  });
}

export function useRejectReview() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (id: string) => rejectReview(id),
    onSuccess: () => {
      invalidateAll(queryClient);
    },
  });
}

export function useRequestReviewChanges() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (id: string) => requestReviewChanges(id),
    onSuccess: () => {
      invalidateAll(queryClient);
    },
  });
}

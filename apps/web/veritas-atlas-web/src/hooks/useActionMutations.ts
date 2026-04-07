import { useMutation, useQueryClient } from "@tanstack/react-query";
import {
  approveCase,
  completeReview,
  rejectCase,
  resolveContradiction,
  submitCase,
} from "../api/actions";

function invalidateCaseQueries(queryClient: ReturnType<typeof useQueryClient>) {
  queryClient.invalidateQueries({ queryKey: ["case-explorer"] });
  queryClient.invalidateQueries({ queryKey: ["cases"] });
}

export function useSubmitCaseAction() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (caseId: string) => submitCase(caseId),
    onSuccess: () => {
      invalidateCaseQueries(queryClient);
    },
  });
}

export function useApproveCaseAction() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (caseId: string) => approveCase(caseId),
    onSuccess: () => {
      invalidateCaseQueries(queryClient);
    },
  });
}

export function useRejectCaseAction() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (caseId: string) => rejectCase(caseId),
    onSuccess: () => {
      invalidateCaseQueries(queryClient);
    },
  });
}

export function useResolveContradictionAction() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (contradictionId: string) => resolveContradiction(contradictionId),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["contradictions"] });
      queryClient.invalidateQueries({ queryKey: ["case-explorer"] });
      queryClient.invalidateQueries({ queryKey: ["cases"] });
    },
  });
}

export function useCompleteReviewAction() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (reviewId: string) => completeReview(reviewId),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["reviews"] });
      queryClient.invalidateQueries({ queryKey: ["case-explorer"] });
      queryClient.invalidateQueries({ queryKey: ["cases"] });
    },
  });
}
import { useMutation } from "@tanstack/react-query";
import {
  approveCase,
  completeReview,
  rejectCase,
  resolveContradiction,
  submitCase,
} from "../api/actions";

export function useSubmitCaseAction() {
  return useMutation({
    mutationFn: (caseId: string) => submitCase(caseId),
  });
}

export function useApproveCaseAction() {
  return useMutation({
    mutationFn: (caseId: string) => approveCase(caseId),
  });
}

export function useRejectCaseAction() {
  return useMutation({
    mutationFn: (caseId: string) => rejectCase(caseId),
  });
}

export function useResolveContradictionAction() {
  return useMutation({
    mutationFn: (contradictionId: string) => resolveContradiction(contradictionId),
  });
}

export function useCompleteReviewAction() {
  return useMutation({
    mutationFn: (reviewId: string) => completeReview(reviewId),
  });
}
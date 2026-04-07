import { useMutation } from "@tanstack/react-query";
import {
  sendClaimToReview,
  returnClaimForEdit,
  escalateContradiction,
  reopenReview,
  preparePublication,
  publishCase,
  holdCase,
} from "../api/workflowActions";

export function useSendClaimToReviewAction() {
  return useMutation({
    mutationFn: (claimId: string) => sendClaimToReview(claimId),
  });
}

export function useReturnClaimForEditAction() {
  return useMutation({
    mutationFn: (claimId: string) => returnClaimForEdit(claimId),
  });
}

export function useEscalateContradictionAction() {
  return useMutation({
    mutationFn: (contradictionId: string) => escalateContradiction(contradictionId),
  });
}

export function useReopenReviewAction() {
  return useMutation({
    mutationFn: (reviewId: string) => reopenReview(reviewId),
  });
}

export function usePreparePublicationAction() {
  return useMutation({
    mutationFn: (caseId: string) => preparePublication(caseId),
  });
}

export function usePublishCaseAction() {
  return useMutation({
    mutationFn: (caseId: string) => publishCase(caseId),
  });
}

export function useHoldCaseAction() {
  return useMutation({
    mutationFn: (caseId: string) => holdCase(caseId),
  });
}
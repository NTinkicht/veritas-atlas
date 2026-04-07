import { useMutation, useQueryClient } from "@tanstack/react-query";
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
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (claimId: string) => sendClaimToReview(claimId),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["claims"] });
    },
  });
}

export function useReturnClaimForEditAction() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (claimId: string) => returnClaimForEdit(claimId),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["claims"] });
    },
  });
}

export function useEscalateContradictionAction() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (contradictionId: string) => escalateContradiction(contradictionId),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["contradictions"] });
    },
  });
}

export function useReopenReviewAction() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (reviewId: string) => reopenReview(reviewId),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["reviews"] });
    },
  });
}

export function usePreparePublicationAction() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (caseId: string) => preparePublication(caseId),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["case-explorer"] });
      queryClient.invalidateQueries({ queryKey: ["cases"] });
    },
  });
}

export function usePublishCaseAction() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (caseId: string) => publishCase(caseId),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["case-explorer"] });
      queryClient.invalidateQueries({ queryKey: ["cases"] });
    },
  });
}

export function useHoldCaseAction() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (caseId: string) => holdCase(caseId),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["case-explorer"] });
      queryClient.invalidateQueries({ queryKey: ["cases"] });
    },
  });
}
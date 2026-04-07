import { useState } from "react";
import { ActionButtonsPanel } from "../components/ActionButtonsPanel";
import { RoleSelectorPanel } from "../components/RoleSelectorPanel";
import {
  useApproveCaseAction,
  useCompleteReviewAction,
  useRejectCaseAction,
  useResolveContradictionAction,
  useSubmitCaseAction,
} from "../hooks/useActionMutations";

export function OperationalActionsPage() {
  const [caseId, setCaseId] = useState("");
  const [contradictionId, setContradictionId] = useState("");
  const [reviewId, setReviewId] = useState("");

  const submitCaseAction = useSubmitCaseAction();
  const approveCaseAction = useApproveCaseAction();
  const rejectCaseAction = useRejectCaseAction();
  const resolveContradictionAction = useResolveContradictionAction();
  const completeReviewAction = useCompleteReviewAction();

  const caseMessage = submitCaseAction.data?.status || approveCaseAction.data?.status || rejectCaseAction.data?.status;
  const caseError =
    (submitCaseAction.error as Error | null)?.message ||
    (approveCaseAction.error as Error | null)?.message ||
    (rejectCaseAction.error as Error | null)?.message;

  const reviewMessage = resolveContradictionAction.data?.status || completeReviewAction.data?.status;
  const reviewError =
    (resolveContradictionAction.error as Error | null)?.message ||
    (completeReviewAction.error as Error | null)?.message;

  return (
    <div style={{ fontFamily: "Arial, sans-serif", padding: 24 }}>
      <h1 style={{ marginTop: 0 }}>Operational Actions</h1>
      <p style={{ color: "#555" }}>
        Manual action surface for submitting, approving, rejecting, resolving, and completing operational items.
      </p>

      <RoleSelectorPanel />

      <div style={{ ...panelStyle, marginTop: 16 }}>
        <label style={labelStyle}>
          Case Id
          <input value={caseId} onChange={(e) => setCaseId(e.target.value)} style={inputStyle} />
        </label>
        <label style={labelStyle}>
          Contradiction Id
          <input value={contradictionId} onChange={(e) => setContradictionId(e.target.value)} style={inputStyle} />
        </label>
        <label style={labelStyle}>
          Review Id
          <input value={reviewId} onChange={(e) => setReviewId(e.target.value)} style={inputStyle} />
        </label>
      </div>

      <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: 16, marginTop: 20 }}>
        <ActionButtonsPanel
          title="Case Actions"
          items={[
            { label: "Submit Case", onClick: () => submitCaseAction.mutate(caseId), disabled: !caseId },
            { label: "Approve Case", onClick: () => approveCaseAction.mutate(caseId), disabled: !caseId },
            { label: "Reject Case", onClick: () => rejectCaseAction.mutate(caseId), disabled: !caseId },
          ]}
          message={caseMessage}
          error={caseError}
          isBusy={submitCaseAction.isPending || approveCaseAction.isPending || rejectCaseAction.isPending}
        />

        <ActionButtonsPanel
          title="Resolution / Review Actions"
          items={[
            { label: "Resolve Contradiction", onClick: () => resolveContradictionAction.mutate(contradictionId), disabled: !contradictionId },
            { label: "Complete Review", onClick: () => completeReviewAction.mutate(reviewId), disabled: !reviewId },
          ]}
          message={reviewMessage}
          error={reviewError}
          isBusy={resolveContradictionAction.isPending || completeReviewAction.isPending}
        />
      </div>
    </div>
  );
}

const panelStyle: React.CSSProperties = {
  border: "1px solid #ddd",
  borderRadius: 14,
  padding: 16,
  display: "grid",
  gap: 12,
};

const labelStyle: React.CSSProperties = {
  display: "grid",
  gap: 6,
};

const inputStyle: React.CSSProperties = {
  border: "1px solid #ccc",
  borderRadius: 10,
  padding: "10px 12px",
  font: "inherit",
};
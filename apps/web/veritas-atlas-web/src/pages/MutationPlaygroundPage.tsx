import { useState } from "react";
import { ActionButtonsPanel } from "../components/ActionButtonsPanel";
import {
  useEscalateContradictionAction,
  useHoldCaseAction,
  usePreparePublicationAction,
  usePublishCaseAction,
  useReopenReviewAction,
  useReturnClaimForEditAction,
  useSendClaimToReviewAction,
} from "../hooks/useWorkflowActions";

export function MutationPlaygroundPage() {
  const [claimId, setClaimId] = useState("");
  const [caseId, setCaseId] = useState("");
  const [contradictionId, setContradictionId] = useState("");
  const [reviewId, setReviewId] = useState("");

  const sendClaim = useSendClaimToReviewAction();
  const returnClaim = useReturnClaimForEditAction();
  const escalate = useEscalateContradictionAction();
  const reopen = useReopenReviewAction();
  const prepare = usePreparePublicationAction();
  const publish = usePublishCaseAction();
  const hold = useHoldCaseAction();

  return (
    <div style={{ fontFamily: "Arial, sans-serif", padding: 24 }}>
      <h1 style={{ marginTop: 0 }}>Mutation Playground</h1>
      <p style={{ color: "#555" }}>
        Playground for exercising review and publication workflow mutations from the frontend.
      </p>

      <div style={gridStyle}>
        <label style={labelStyle}>
          Claim Id
          <input value={claimId} onChange={(e) => setClaimId(e.target.value)} style={inputStyle} />
        </label>
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
          title="Review Routing Mutations"
          items={[
            { label: "Send Claim To Review", onClick: () => sendClaim.mutate(claimId), disabled: !claimId },
            { label: "Return Claim For Edit", onClick: () => returnClaim.mutate(claimId), disabled: !claimId },
            { label: "Escalate Contradiction", onClick: () => escalate.mutate(contradictionId), disabled: !contradictionId },
            { label: "Reopen Review", onClick: () => reopen.mutate(reviewId), disabled: !reviewId },
          ]}
          message={sendClaim.data?.status || returnClaim.data?.status || escalate.data?.status || reopen.data?.status}
        />

        <ActionButtonsPanel
          title="Publication Mutations"
          items={[
            { label: "Prepare Publication", onClick: () => prepare.mutate(caseId), disabled: !caseId },
            { label: "Publish Case", onClick: () => publish.mutate(caseId), disabled: !caseId },
            { label: "Hold Case", onClick: () => hold.mutate(caseId), disabled: !caseId },
          ]}
          message={prepare.data?.status || publish.data?.status || hold.data?.status}
        />
      </div>
    </div>
  );
}

const gridStyle: React.CSSProperties = {
  display: "grid",
  gridTemplateColumns: "1fr 1fr",
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
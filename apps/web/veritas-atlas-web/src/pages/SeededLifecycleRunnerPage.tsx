import { useState } from "react";
import { useWorkflowSeed } from "../hooks/useWorkflowSeed";
import {
  usePreparePublicationAction,
  usePublishCaseAction,
} from "../hooks/useWorkflowActions";
import { useResolveContradictionAction } from "../hooks/useActionMutations";

export function SeededLifecycleRunnerPage() {
  const [caseId, setCaseId] = useState("");
  const [contradictionId, setContradictionId] = useState("");

  const seedMutation = useWorkflowSeed();
  const prepareMutation = usePreparePublicationAction();
  const publishMutation = usePublishCaseAction();
  const resolveMutation = useResolveContradictionAction();

  const handleSeed = () => {
    seedMutation.mutate(undefined, {
      onSuccess: (data) => {
        setCaseId(data.caseId);
        setContradictionId(data.contradictionId);
      },
    });
  };

  return (
    <div style={{ fontFamily: "Arial, sans-serif", padding: 24 }}>
      <h1 style={{ marginTop: 0 }}>Seeded Lifecycle Runner</h1>
      <p style={{ color: "#555" }}>
        Seed and execute a minimal lifecycle directly from the frontend.
      </p>

      <div style={panelStyle}>
        <button onClick={handleSeed} style={buttonStyle}>Seed Lifecycle</button>
        <button onClick={() => prepareMutation.mutate(caseId)} disabled={!caseId} style={buttonStyle}>Prepare Publication</button>
        <button onClick={() => resolveMutation.mutate(contradictionId)} disabled={!contradictionId} style={buttonStyle}>Resolve Contradiction</button>
        <button onClick={() => publishMutation.mutate(caseId)} disabled={!caseId} style={buttonStyle}>Publish Case</button>
      </div>

      <div style={{ ...panelStyle, marginTop: 16 }}>
        <p>Case Id: {caseId || "N/A"}</p>
        <p>Contradiction Id: {contradictionId || "N/A"}</p>
        <p>{seedMutation.data ? `Seeded case ${seedMutation.data.caseId}` : ""}</p>
        <p>{prepareMutation.data ? `Prepare status: ${prepareMutation.data.status}` : ""}</p>
        <p>{resolveMutation.data ? `Resolve status: ${resolveMutation.data.status}` : ""}</p>
        <p>{publishMutation.data ? `Publish status: ${publishMutation.data.status}` : ""}</p>
      </div>
    </div>
  );
}

const panelStyle: React.CSSProperties = {
  border: "1px solid #ddd",
  borderRadius: 14,
  padding: 16,
  display: "flex",
  gap: 12,
  flexWrap: "wrap",
};

const buttonStyle: React.CSSProperties = {
  border: "1px solid #bbb",
  borderRadius: 10,
  padding: "10px 14px",
  background: "white",
  cursor: "pointer",
  font: "inherit",
};
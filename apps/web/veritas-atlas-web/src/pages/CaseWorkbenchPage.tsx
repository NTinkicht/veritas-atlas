import { Link, useParams } from "react-router-dom";
import { useCaseWorkbench } from "../hooks/useCaseWorkbench";
import { ContradictionQueuePanel } from "../components/ContradictionQueuePanel";
import { ActionButtonsPanel } from "../components/ActionButtonsPanel";
import {
  useApproveCaseAction,
  useRejectCaseAction,
  useSubmitCaseAction,
} from "../hooks/useActionMutations";

export function CaseWorkbenchPage() {
  const { id } = useParams();
  const { caseQuery, linkedClaims, contradictionItems, claimsQuery, contradictionsQuery } = useCaseWorkbench(id);
  const submitCaseAction = useSubmitCaseAction();
  const approveCaseAction = useApproveCaseAction();
  const rejectCaseAction = useRejectCaseAction();

  if (caseQuery.isLoading) {
    return <div style={{ fontFamily: "Arial, sans-serif", padding: 24 }}>Loading case workbench...</div>;
  }

  if (caseQuery.isError) {
    return <div style={{ fontFamily: "Arial, sans-serif", padding: 24, color: "crimson" }}>Failed to load case: {(caseQuery.error as Error).message}</div>;
  }

  if (!caseQuery.data) {
    return <div style={{ fontFamily: "Arial, sans-serif", padding: 24 }}>Case not found.</div>;
  }

  const item = caseQuery.data;

  return (
    <div style={{ fontFamily: "Arial, sans-serif", padding: 24 }}>
      <nav style={{ display: "flex", gap: 16, marginBottom: 20, flexWrap: "wrap" }}>
        <Link to="/case-explorer">Back to Case Explorer</Link>
        <Link to={`/cases/${item.id}`}>Case Detail</Link>
        <Link to={`/contradictions?caseId=${item.id}`}>Contradictions for Case</Link>
      </nav>

      <h1 style={{ marginTop: 0 }}>Case Workbench</h1>

      <div style={panelStyle}>
        <Row label="Case Id" value={item.id} />
        <Row label="Status" value={item.status} />
        <Row label="Created" value={new Date(item.createdAtUtc).toLocaleString()} />
      </div>

      <div style={gridStyle}>
        <div style={panelStyle}>
          <h3 style={{ marginTop: 0 }}>Linked Claims</h3>
          {claimsQuery.isLoading && <p>Loading claims...</p>}
          {!claimsQuery.isLoading && linkedClaims.length === 0 && <p>No linked claims.</p>}
          {linkedClaims.length > 0 && (
            <ul style={{ marginBottom: 0 }}>
              {linkedClaims.map((claim) => (
                <li key={claim.id}>
                  <Link to={`/claims/${claim.id}`}>{claim.topic}</Link> - {claim.type} - {claim.status}
                </li>
              ))}
            </ul>
          )}
        </div>

        <ContradictionQueuePanel items={contradictionItems} />
      </div>

      <div style={{ ...panelStyle, marginTop: 20 }}>
        <h3 style={{ marginTop: 0 }}>Operational Actions</h3>
        <div style={{ display: "flex", gap: 12, flexWrap: "wrap" }}>
          <Link to="/claims">Open Claims Catalog</Link>
          <Link to={`/contradictions?caseId=${item.id}`}>Open Contradictions Catalog</Link>
          <Link to="/resolution-board">Open Resolution Board</Link>
        </div>
        {contradictionsQuery.isLoading && <p style={{ marginTop: 12 }}>Refreshing contradictions...</p>}
      </div>

      <div style={{ marginTop: 20 }}>
        <ActionButtonsPanel
          title="Case Actions"
          items={[
            { label: "Submit Case", onClick: () => submitCaseAction.mutate(item.id) },
            { label: "Approve Case", onClick: () => approveCaseAction.mutate(item.id) },
            { label: "Reject Case", onClick: () => rejectCaseAction.mutate(item.id) },
          ]}
          message={
            submitCaseAction.data?.status ||
            approveCaseAction.data?.status ||
            rejectCaseAction.data?.status ||
            undefined
          }
        />
      </div>
    </div>
  );
}

function Row({ label, value }: { label: string; value: string }) {
  return (
    <div style={{ display: "grid", gridTemplateColumns: "180px 1fr", gap: 12, padding: "6px 0" }}>
      <strong>{label}</strong>
      <span>{value}</span>
    </div>
  );
}

const gridStyle: React.CSSProperties = {
  display: "grid",
  gridTemplateColumns: "1fr 1fr",
  gap: 16,
};

const panelStyle: React.CSSProperties = {
  border: "1px solid #ddd",
  borderRadius: 14,
  padding: 16,
};
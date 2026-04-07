import { Link, useParams } from "react-router-dom";
import { useContradictionDetail } from "../hooks/useContradictionDetail";
import { ResolutionActionsPanel } from "../components/ResolutionActionsPanel";
import { ActionButtonsPanel } from "../components/ActionButtonsPanel";
import { useResolveContradictionAction } from "../hooks/useActionMutations";

export function ContradictionResolutionWorkspacePage() {
  const { id } = useParams();
  const query = useContradictionDetail(id);

  if (query.isLoading) {
    return <div style={{ fontFamily: "Arial, sans-serif", padding: 24 }}>Loading contradiction workspace...</div>;
  }

  if (query.isError) {
    return <div style={{ fontFamily: "Arial, sans-serif", padding: 24, color: "crimson" }}>Failed to load contradiction: {(query.error as Error).message}</div>;
  }

  if (!query.data) {
    return <div style={{ fontFamily: "Arial, sans-serif", padding: 24 }}>Contradiction not found.</div>;
  }

  const item = query.data;
  const resolveAction = useResolveContradictionAction();

  return (
    <div style={{ fontFamily: "Arial, sans-serif", padding: 24 }}>
      <nav style={{ display: "flex", gap: 16, marginBottom: 20, flexWrap: "wrap" }}>
        <Link to="/truth-review-studio">Back to Truth Review Studio</Link>
        <Link to={`/contradictions/${item.id}`}>Contradiction Detail</Link>
        <Link to={`/case-explorer/${item.caseId}`}>Case Workbench</Link>
      </nav>

      <h1 style={{ marginTop: 0 }}>Contradiction Resolution Workspace</h1>

      <div style={panelStyle}>
        <Row label="Contradiction Id" value={item.id} />
        <Row label="Topic" value={item.topic} />
        <Row label="Summary" value={item.summary} />
        <Row label="Severity" value={item.severity} />
        <Row label="Status" value={item.status} />
        <Row label="Case Id" value={item.caseId ?? "N/A"} />
      </div>

      <div style={{ ...panelStyle, marginTop: 20 }}>
        <h3 style={{ marginTop: 0 }}>Resolution Notes</h3>
        <ul style={{ marginBottom: 0 }}>
          <li>Confirm opposing claims belong to the same decision space</li>
          <li>Assess whether contradiction is direct, temporal, or contextual</li>
          <li>Route unresolved contradiction to review queue</li>
          <li>Route resolved contradiction toward publication decision</li>
        </ul>
      </div>

      <div style={{ marginTop: 20 }}>
        <ResolutionActionsPanel contradictionId={item.id} caseId={item.caseId ?? undefined} />
      </div>

      <div style={{ marginTop: 20 }}>
        <ActionButtonsPanel
          title="Contradiction Resolution Action"
          items={[
            { label: "Resolve Contradiction", onClick: () => resolveAction.mutate(item.id) },
          ]}
          message={resolveAction.data?.status}
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

const panelStyle: React.CSSProperties = {
  border: "1px solid #ddd",
  borderRadius: 14,
  padding: 16,
};
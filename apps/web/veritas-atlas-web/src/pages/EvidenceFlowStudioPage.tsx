import { Link } from "react-router-dom";
import { useEvidenceList } from "../hooks/useEvidenceList";
import { useStatements } from "../hooks/useStatements";
import { useClaims } from "../hooks/useClaims";
import { useContradictions } from "../hooks/useContradictions";
import { FlowHealthPanel } from "../components/FlowHealthPanel";
import { WorkspaceCoordinationPanel } from "../components/WorkspaceCoordinationPanel";

export function EvidenceFlowStudioPage() {
  const evidenceQuery = useEvidenceList();
  const statementsQuery = useStatements();
  const claimsQuery = useClaims();
  const contradictionsQuery = useContradictions();

  const evidenceCount = evidenceQuery.data?.items.length ?? 0;
  const statementCount = statementsQuery.data?.items.length ?? 0;
  const claimCount = claimsQuery.data?.items.length ?? 0;
  const contradictionCount = contradictionsQuery.data?.items.length ?? 0;

  return (
    <div style={{ fontFamily: "Arial, sans-serif", padding: 24 }}>
      <header style={{ marginBottom: 24 }}>
        <h1 style={{ margin: 0 }}>Evidence Flow Studio</h1>
        <p style={{ color: "#555" }}>
          End-to-end operational view from evidence intake to contradiction handling and decision readiness.
        </p>

        <nav style={{ display: "flex", gap: 16, marginTop: 12, flexWrap: "wrap" }}>
          <Link to="/">Home</Link>
          <Link to="/evidence-flow-studio">Evidence Flow Studio</Link>
          <Link to="/evidence-trace">Evidence Trace</Link>
          <Link to="/decision-intelligence">Decision Intelligence</Link>
        </nav>
      </header>

      <div style={gridStyle}>
        <FlowHealthPanel
          evidenceCount={evidenceCount}
          statementCount={statementCount}
          claimCount={claimCount}
          contradictionCount={contradictionCount}
        />
        <WorkspaceCoordinationPanel />
      </div>

      <div style={{ ...panelStyle, marginTop: 20 }}>
        <h3 style={{ marginTop: 0 }}>Flow Summary</h3>
        <ol style={{ marginBottom: 0, paddingLeft: 20 }}>
          <li>Evidence intake and trace verification</li>
          <li>Statement extraction and normalization</li>
          <li>Claim formulation and review</li>
          <li>Contradiction creation and resolution</li>
          <li>Decision and publication routing</li>
        </ol>
      </div>
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
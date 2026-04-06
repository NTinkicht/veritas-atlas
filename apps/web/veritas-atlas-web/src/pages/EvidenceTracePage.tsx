import { Link } from "react-router-dom";
import { useStatements } from "../hooks/useStatements";
import { useClaims } from "../hooks/useClaims";
import { EvidenceTracePanel } from "../components/EvidenceTracePanel";

export function EvidenceTracePage() {
  const statementsQuery = useStatements();
  const claimsQuery = useClaims();

  const statementNodes = (statementsQuery.data?.items ?? []).slice(0, 4).map((item) => ({
    id: item.id,
    label: "Statement",
    detail: item.text,
  }));

  const claimNodes = (claimsQuery.data?.items ?? []).slice(0, 4).map((item) => ({
    id: item.id,
    label: "Claim",
    detail: item.topic,
  }));

  const nodes = [...statementNodes, ...claimNodes];

  return (
    <div style={{ fontFamily: "Arial, sans-serif", padding: 24 }}>
      <header style={{ marginBottom: 24 }}>
        <h1 style={{ margin: 0 }}>Evidence Trace</h1>
        <p style={{ color: "#555" }}>
          Trace surface from extracted statements into claims and downstream contradiction work.
        </p>

        <nav style={{ display: "flex", gap: 16, marginTop: 12, flexWrap: "wrap" }}>
          <Link to="/evidence">Evidence</Link>
          <Link to="/statements">Statements</Link>
          <Link to="/claims">Claims</Link>
          <Link to="/evidence-trace">Evidence Trace</Link>
        </nav>
      </header>

      <EvidenceTracePanel nodes={nodes} />
    </div>
  );
}
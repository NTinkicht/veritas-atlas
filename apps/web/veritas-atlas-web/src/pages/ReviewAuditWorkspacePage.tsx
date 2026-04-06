import { Link } from "react-router-dom";
import { useClaims } from "../hooks/useClaims";
import { useContradictions } from "../hooks/useContradictions";
import { ReviewAuditPanel } from "../components/ReviewAuditPanel";

export function ReviewAuditWorkspacePage() {
  const claimsQuery = useClaims();
  const contradictionsQuery = useContradictions();

  const items = [
    ...(claimsQuery.data?.items ?? []).slice(0, 4).map((item) => ({
      id: item.id,
      label: item.topic,
      outcome: item.status,
    })),
    ...(contradictionsQuery.data?.items ?? []).slice(0, 4).map((item) => ({
      id: item.id,
      label: item.topic,
      outcome: item.status,
    })),
  ];

  return (
    <div style={{ fontFamily: "Arial, sans-serif", padding: 24 }}>
      <header style={{ marginBottom: 24 }}>
        <h1 style={{ margin: 0 }}>Review Audit Workspace</h1>
        <p style={{ color: "#555" }}>
          Audit-oriented view across review outcomes, contradictions, and decision readiness.
        </p>
        <nav style={{ display: "flex", gap: 16, marginTop: 12, flexWrap: "wrap" }}>
          <Link to="/review-audit">Review Audit</Link>
          <Link to="/review-decision-board">Review Decision Board</Link>
          <Link to="/decision-log">Decision Log</Link>
        </nav>
      </header>

      <ReviewAuditPanel items={items} />
    </div>
  );
}
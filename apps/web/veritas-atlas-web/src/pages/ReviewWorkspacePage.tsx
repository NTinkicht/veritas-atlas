import { Link, useSearchParams } from "react-router-dom";
import { useClaimDetail } from "../hooks/useClaimDetail";

export function ReviewWorkspacePage() {
  const [params] = useSearchParams();
  const claimId = params.get("claimId") ?? "";
  const claimQuery = useClaimDetail(claimId || undefined);

  return (
    <div style={{ padding: 24, fontFamily: "Arial, sans-serif", maxWidth: 980 }}>
      <h1>Review Workspace</h1>
      <p>Focused review surface for claim validation, escalation, and publication readiness.</p>

      <div style={{ display: "flex", gap: 12, flexWrap: "wrap", marginBottom: 20 }}>
        <Link to="/review-queue">Review Queue</Link>
        <Link to="/publication-desk">Publication Desk</Link>
        {claimId && <Link to={`/claims/${claimId}`}>Claim</Link>}
      </div>

      <div style={panelStyle}>
        <Row label="Claim Id" value={claimId || "N/A"} />
        <Row label="Workspace Status" value="Ready for structured human review" />
      </div>

      {claimId && claimQuery.isLoading && <p>Loading claim context...</p>}
      {claimId && claimQuery.isError && <p style={{ color: "crimson" }}>Failed to load claim context.</p>}

      {claimId && claimQuery.isSuccess && claimQuery.data && (
        <>
          <div style={panelStyle}>
            <h2 style={{ marginTop: 0 }}>Claim Context</h2>
            <Row label="Topic" value={claimQuery.data.topic} />
            <Row label="Type" value={claimQuery.data.type} />
            <Row label="Status" value={claimQuery.data.status} />
            <Row label="Material" value={claimQuery.data.isMaterial ? "Yes" : "No"} />
            <Row label="Normalized Text" value={claimQuery.data.normalizedText} />
          </div>

          <div style={panelStyle}>
            <h2 style={{ marginTop: 0 }}>Review actions</h2>
            <ul style={{ marginBottom: 0 }}>
              <li>Validate claim formulation</li>
              <li>Check evidence alignment</li>
              <li>Escalate to contradiction comparison</li>
              <li>Mark ready for publication flow</li>
            </ul>
          </div>
        </>
      )}
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
  marginBottom: 20,
};
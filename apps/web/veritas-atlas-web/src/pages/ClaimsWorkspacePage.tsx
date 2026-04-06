import { Link, useSearchParams } from "react-router-dom";
import { useStatementDetail } from "../hooks/useStatementDetail";

export function ClaimsWorkspacePage() {
  const [params] = useSearchParams();
  const statementId = params.get("statementId") ?? "";
  const statementQuery = useStatementDetail(statementId || undefined);

  return (
    <div style={{ fontFamily: "Arial, sans-serif", padding: "24px", maxWidth: "980px" }}>
      <header style={{ marginBottom: "24px" }}>
        <h1 style={{ margin: 0 }}>Claims Workspace</h1>
        <p style={{ color: "#555" }}>Working context page for the next claim slice.</p>
        <nav style={{ display: "flex", gap: "16px", marginTop: "12px", flexWrap: "wrap" }}>
          <Link to="/">Home</Link>
          <Link to="/statements">Statements</Link>
          {statementId && <Link to={`/statements/${statementId}`}>Statement</Link>}
        </nav>
      </header>

      <div style={cardStyle}>
        <Row label="Statement Id" value={statementId || "N/A"} />
        <Row label="Workspace Status" value="Ready for claim implementation" />
        <Row label="Current Scope" value="Statement context, navigation, and planning" />
      </div>

      {statementId.length === 0 && (
        <div style={cardStyle}>
          <h3 style={{ marginTop: 0 }}>No statement selected</h3>
          <p style={{ marginBottom: 0 }}>
            Open this page from a statement detail page, or pass <code>?statementId=...</code>.
          </p>
        </div>
      )}

      {statementId.length > 0 && statementQuery.isLoading && (
        <div style={cardStyle}>
          <p style={{ margin: 0 }}>Loading statement context...</p>
        </div>
      )}

      {statementId.length > 0 && statementQuery.isError && (
        <div style={cardStyle}>
          <p style={{ margin: 0, color: "crimson" }}>
            Failed to load statement context: {(statementQuery.error as Error).message}
          </p>
        </div>
      )}

      {statementId.length > 0 && statementQuery.isSuccess && statementQuery.data && (
        <>
          <div style={cardStyle}>
            <h3 style={{ marginTop: 0 }}>Statement Context</h3>
            <Row label="Text" value={statementQuery.data.text} />
            <Row label="Topic" value={statementQuery.data.topic ?? "N/A"} />
            <Row label="Predicate" value={statementQuery.data.predicate ?? "N/A"} />
            <Row label="Object" value={statementQuery.data.object ?? "N/A"} />
            <Row label="Polarity" value={statementQuery.data.polarity} />
            <Row label="Status" value={statementQuery.data.status} />
            <Row label="Evidence Id" value={statementQuery.data.evidenceId ?? "N/A"} />
          </div>

          <div style={cardStyle}>
            <h3 style={{ marginTop: 0 }}>Planned Claim Actions</h3>
            <ul style={{ marginBottom: 0 }}>
              <li>Create claim from the statement text</li>
              <li>Classify claim type and confidence</li>
              <li>Link claim to contradictions and review workflows</li>
            </ul>
          </div>

          <div style={{ display: "flex", gap: "12px", flexWrap: "wrap" }}>
            <Link to={`/statements/${statementQuery.data.id}`} style={actionLinkStyle}>Back to Statement</Link>
            {statementQuery.data.evidenceId && (
              <Link to={`/evidence/${statementQuery.data.evidenceId}`} style={actionLinkStyle}>Open Evidence</Link>
            )}
            <Link to={`/statements?topic=${encodeURIComponent(statementQuery.data.topic ?? "")}`} style={actionLinkStyle}>Explore Similar Statements</Link>
          </div>
        </>
      )}
    </div>
  );
}

function Row({ label, value }: { label: string; value: string }) {
  return (
    <div style={{ display: "grid", gridTemplateColumns: "180px 1fr", gap: "12px", padding: "6px 0" }}>
      <strong>{label}</strong>
      <span>{value}</span>
    </div>
  );
}

const cardStyle: React.CSSProperties = {
  border: "1px solid #ddd",
  borderRadius: "12px",
  padding: "16px",
  marginBottom: "16px",
};

const actionLinkStyle: React.CSSProperties = {
  padding: "10px 16px",
  borderRadius: "8px",
  border: "1px solid #1976d2",
  textDecoration: "none",
  color: "inherit",
};

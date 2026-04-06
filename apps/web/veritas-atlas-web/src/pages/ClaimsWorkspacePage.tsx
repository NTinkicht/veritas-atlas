import { Link, useSearchParams } from "react-router-dom";
import { useEffect, useState } from "react";
import { useStatementDetail } from "../hooks/useStatementDetail";
import { useClaims } from "../hooks/useClaims";
import { useCreateClaim } from "../hooks/useCreateClaim";

export function ClaimsWorkspacePage() {
  const [params] = useSearchParams();
  const statementId = params.get("statementId") ?? "";

  const statementQuery = useStatementDetail(statementId || undefined);
  const claimsQuery = useClaims(statementId || undefined);
  const createClaimMutation = useCreateClaim();

  const [topic, setTopic] = useState("");
  const [normalizedText, setNormalizedText] = useState("");
  const [type, setType] = useState("Factual");
  const [isMaterial, setIsMaterial] = useState(true);

  useEffect(() => {
    if (statementQuery.isSuccess && statementQuery.data) {
      setNormalizedText((current) => current || statementQuery.data.text);
      setTopic((current) => current || statementQuery.data.topic || "general");
    }
  }, [statementQuery.isSuccess, statementQuery.data]);

  const submit = async (event: React.FormEvent) => {
    event.preventDefault();

    await createClaimMutation.mutateAsync({
      statementId,
      topic,
      normalizedText,
      type,
      isMaterial,
    });
  };

  return (
    <div style={{ fontFamily: "Arial, sans-serif", padding: "24px", maxWidth: "980px" }}>
      <header style={{ marginBottom: "24px" }}>
        <h1 style={{ margin: 0 }}>Claims Workspace</h1>
        <p style={{ color: "#555" }}>Create and inspect claims for a statement.</p>
        <nav style={{ display: "flex", gap: "16px", marginTop: "12px", flexWrap: "wrap" }}>
          <Link to="/">Home</Link>
          <Link to="/statements">Statements</Link>
          <Link to="/claims">Claims</Link>
          <Link to="/contradictions/workspace">Contradictions Workspace</Link>
          {statementId && <Link to={`/statements/${statementId}`}>Statement</Link>}
        </nav>
      </header>

      <div style={cardStyle}>
        <Row label="Statement Id" value={statementId || "N/A"} />
      </div>

      {statementId.length > 0 && statementQuery.isSuccess && statementQuery.data && (
        <div style={cardStyle}>
          <h3 style={{ marginTop: 0 }}>Statement Context</h3>
          <Row label="Text" value={statementQuery.data.text} />
          <Row label="Topic" value={statementQuery.data.topic ?? "N/A"} />
          <Row label="Polarity" value={statementQuery.data.polarity} />
          <Row label="Status" value={statementQuery.data.status} />
          <div style={{ display: "flex", gap: "12px", flexWrap: "wrap", marginTop: "12px" }}>
            {statementQuery.data.evidenceId && (
              <Link to={`/evidence/${statementQuery.data.evidenceId}`} style={actionLinkStyle}>Open Evidence</Link>
            )}
            <Link to={`/claims?statementId=${statementQuery.data.id}`} style={actionLinkStyle}>Open Claims for Statement</Link>
            <Link to={`/contradictions/workspace?statementId=${statementQuery.data.id}`} style={actionLinkStyle}>Prepare Contradiction Review</Link>
          </div>
        </div>
      )}

      {statementId.length > 0 && (
        <div style={cardStyle}>
          <h3 style={{ marginTop: 0 }}>Create Claim</h3>
          <form onSubmit={submit} style={{ display: "grid", gap: "16px" }}>
            <label style={labelStyle}>
              <span>Topic</span>
              <input value={topic} onChange={(e) => setTopic(e.target.value)} style={inputStyle} required />
            </label>

            <label style={labelStyle}>
              <span>Normalized Text</span>
              <textarea value={normalizedText} onChange={(e) => setNormalizedText(e.target.value)} style={{ ...inputStyle, minHeight: "120px", resize: "vertical" }} required />
            </label>

            <label style={labelStyle}>
              <span>Type</span>
              <select value={type} onChange={(e) => setType(e.target.value)} style={inputStyle}>
                <option value="Factual">Factual</option>
                <option value="Temporal">Temporal</option>
                <option value="Quantitative">Quantitative</option>
                <option value="Categorical">Categorical</option>
                <option value="Attribution">Attribution</option>
              </select>
            </label>

            <label style={{ display: "flex", gap: "10px", alignItems: "center" }}>
              <input type="checkbox" checked={isMaterial} onChange={(e) => setIsMaterial(e.target.checked)} />
              <span>Material claim</span>
            </label>

            <div style={{ display: "flex", gap: "12px", flexWrap: "wrap" }}>
              <button type="submit" style={buttonStyle} disabled={createClaimMutation.isPending}>
                {createClaimMutation.isPending ? "Creating..." : "Create Claim"}
              </button>
              <Link to={statementId ? `/claims?statementId=${statementId}` : "/claims"} style={linkButtonStyle}>Open Claims</Link>
            </div>
          </form>

          {createClaimMutation.isError && (
            <p style={{ color: "crimson", marginTop: "16px" }}>
              Failed to create claim: {(createClaimMutation.error as Error).message}
            </p>
          )}

          {createClaimMutation.isSuccess && (
            <div style={successCardStyle}>
              <p style={{ marginTop: 0 }}><strong>Claim created successfully.</strong></p>
              <p style={{ marginBottom: "8px" }}>New claim id: {createClaimMutation.data.id}</p>
              <div style={{ display: "flex", gap: "12px", flexWrap: "wrap" }}>
                <Link to={`/claims/${createClaimMutation.data.id}`}>Open Claim Detail</Link>
                <Link to={`/claims?statementId=${createClaimMutation.data.statementId}`}>Open Claim List</Link>
                <Link to={`/contradictions/workspace?claimId=${createClaimMutation.data.id}&statementId=${createClaimMutation.data.statementId}`}>Open Contradictions Workspace</Link>
              </div>
            </div>
          )}
        </div>
      )}

      <div style={cardStyle}>
        <h3 style={{ marginTop: 0 }}>Claims for this Statement</h3>

        {claimsQuery.isLoading && <p>Loading claims...</p>}
        {claimsQuery.isError && <p style={{ color: "crimson" }}>Failed to load claims: {(claimsQuery.error as Error).message}</p>}
        {claimsQuery.isSuccess && claimsQuery.data.items.length === 0 && <p>No claims yet.</p>}
        {claimsQuery.isSuccess && claimsQuery.data.items.length > 0 && (
          <ul>
            {claimsQuery.data.items.map((claim) => (
              <li key={claim.id}>
                <Link to={`/claims/${claim.id}`}>{claim.topic}</Link> - {claim.type} - {claim.status}
              </li>
            ))}
          </ul>
        )}
      </div>
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

const labelStyle: React.CSSProperties = {
  display: "grid",
  gap: "8px",
};

const inputStyle: React.CSSProperties = {
  width: "100%",
  boxSizing: "border-box",
  padding: "10px 12px",
  borderRadius: "8px",
  border: "1px solid #ccc",
  font: "inherit",
};

const buttonStyle: React.CSSProperties = {
  padding: "10px 16px",
  borderRadius: "8px",
  border: "1px solid #1976d2",
  background: "#1976d2",
  color: "white",
  cursor: "pointer",
};

const linkButtonStyle: React.CSSProperties = {
  padding: "10px 16px",
  borderRadius: "8px",
  border: "1px solid #1976d2",
  textDecoration: "none",
  color: "inherit",
  display: "inline-flex",
  alignItems: "center",
};

const actionLinkStyle: React.CSSProperties = {
  padding: "10px 16px",
  borderRadius: "8px",
  border: "1px solid #1976d2",
  textDecoration: "none",
  color: "inherit",
};

const successCardStyle: React.CSSProperties = {
  marginTop: "20px",
  padding: "16px",
  border: "1px solid #d7e8d7",
  borderRadius: "12px",
  background: "#f8fff8",
};

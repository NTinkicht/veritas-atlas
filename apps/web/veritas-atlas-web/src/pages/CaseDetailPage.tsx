import { Link, useParams } from "react-router-dom";
import { useCaseDetail } from "../hooks/useCaseDetail";
import { useClaims } from "../hooks/useClaims";
import { useCreateClaim } from "../hooks/useCreateClaim";

export function CaseDetailPage() {
  const { id } = useParams();
  const caseQuery = useCaseDetail(id);
  const claimsQuery = useClaims();
  const createClaimMutation = useCreateClaim();

  if (caseQuery.isLoading) {
    return <div style={{ fontFamily: "Arial, sans-serif", padding: "24px" }}>Loading case...</div>;
  }

  if (caseQuery.isError) {
    return <div style={{ fontFamily: "Arial, sans-serif", padding: "24px", color: "crimson" }}>Failed to load case: {(caseQuery.error as Error).message}</div>;
  }

  if (!caseQuery.data) {
    return <div style={{ fontFamily: "Arial, sans-serif", padding: "24px" }}>Case not found.</div>;
  }

  const item = caseQuery.data;
  const linkedClaims = (claimsQuery.data?.items ?? []).filter((x) => x.caseId === item.id);

  const createLinkedClaim = async () => {
    const statementId = window.prompt("Statement Id to link to this case:");
    const normalizedText = window.prompt("Claim text:");
    if (!statementId || !normalizedText) {
      return;
    }

    await createClaimMutation.mutateAsync({
      statementId,
      topic: "case-linked claim",
      normalizedText,
      caseId: item.id,
      isMaterial: true,
    });
  };

  return (
    <div style={{ fontFamily: "Arial, sans-serif", padding: "24px" }}>
      <nav style={{ display: "flex", gap: "16px", marginBottom: "20px", flexWrap: "wrap" }}>
        <Link to="/cases">Back to Cases</Link>
        <Link to="/claims">Claims</Link>
      </nav>

      <h1 style={{ marginTop: 0 }}>Case</h1>

      <div style={cardStyle}>
        <Row label="Id" value={item.id} />
        <Row label="Status" value={item.status} />
        <Row label="Created By" value={"N/A"} />
        <Row label="Created" value={new Date(item.createdAtUtc).toLocaleString()} />
      </div>

      <div style={cardStyle}>
        <h3 style={{ marginTop: 0 }}>Claims linked to this case</h3>

        <div style={{ display: "flex", gap: "12px", flexWrap: "wrap", marginBottom: "12px" }}>
          <button type="button" style={buttonStyle} onClick={createLinkedClaim} disabled={createClaimMutation.isPending}>
            {createClaimMutation.isPending ? "Creating..." : "Create Linked Claim"}
          </button>
        </div>

        {createClaimMutation.isError && (
          <p style={{ color: "crimson" }}>
            Failed to create claim: {(createClaimMutation.error as Error).message}
          </p>
        )}

        {claimsQuery.isLoading && <p>Loading claims...</p>}
        {claimsQuery.isError && <p style={{ color: "crimson" }}>Failed to load claims: {(claimsQuery.error as Error).message}</p>}
        {claimsQuery.isSuccess && linkedClaims.length === 0 && <p>No claims linked to this case yet.</p>}
        {claimsQuery.isSuccess && linkedClaims.length > 0 && (
          <ul>
            {linkedClaims.map((claim) => (
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

const buttonStyle: React.CSSProperties = {
  padding: "10px 16px",
  borderRadius: "8px",
  border: "1px solid #1976d2",
  background: "#1976d2",
  color: "white",
  cursor: "pointer",
};


import { Link } from "react-router-dom";
import { useClaims } from "../hooks/useClaims";

export function PublicationDeskPage() {
  const claimsQuery = useClaims();
  const publicationCandidates = (claimsQuery.data?.items ?? []).slice(0, 8);

  return (
    <div style={{ padding: 24, fontFamily: "Arial, sans-serif" }}>
      <h1>Publication Desk</h1>
      <p>Operational surface for publication-ready outputs and final publication checks.</p>

      <div style={{ display: "flex", gap: 12, flexWrap: "wrap", marginBottom: 20 }}>
        <Link to="/review-queue">Review Queue</Link>
        <Link to="/review-workspace">Review Workspace</Link>
        <Link to="/claims">Claims</Link>
      </div>

      <section style={panelStyle}>
        <h2 style={{ marginTop: 0 }}>Publication readiness</h2>
        <ul style={{ marginBottom: 0 }}>
          <li>Claim quality and wording review</li>
          <li>Evidence alignment check</li>
          <li>Contradiction notes attached</li>
          <li>Publication routing placeholder</li>
        </ul>
      </section>

      <section style={panelStyle}>
        <h2 style={{ marginTop: 0 }}>Candidate items</h2>
        {claimsQuery.isLoading && <p>Loading publication candidates...</p>}
        {claimsQuery.isError && <p style={{ color: "crimson" }}>Failed to load publication candidates.</p>}
        {claimsQuery.isSuccess && publicationCandidates.length === 0 && <p>No publication candidates yet.</p>}
        {claimsQuery.isSuccess && publicationCandidates.length > 0 && (
          <ul style={{ marginBottom: 0 }}>
            {publicationCandidates.map((item) => (
              <li key={item.id}>
                <Link to={`/claims/${item.id}`}>{item.topic}</Link> - {item.type} - {item.status}
              </li>
            ))}
          </ul>
        )}
      </section>
    </div>
  );
}

const panelStyle: React.CSSProperties = {
  border: "1px solid #ddd",
  borderRadius: 14,
  padding: 16,
  marginBottom: 20,
};
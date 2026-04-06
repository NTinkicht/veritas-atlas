import { Link } from "react-router-dom";
import { useClaims } from "../hooks/useClaims";

export function ReviewQueuePage() {
  const claimsQuery = useClaims();

  const queueItems = (claimsQuery.data?.items ?? []).slice(0, 12);

  return (
    <div style={{ padding: 24, fontFamily: "Arial, sans-serif" }}>
      <h1>Review Queue</h1>
      <p>Operational queue for human review across claims and contradiction preparation.</p>

      <div style={{ display: "flex", gap: 12, flexWrap: "wrap", marginBottom: 20 }}>
        <Link to="/reviews">Reviews</Link>
        <Link to="/review-workspace">Review Workspace</Link>
        <Link to="/publication-desk">Publication Desk</Link>
        <Link to="/claims">Claims</Link>
      </div>

      <section style={panelStyle}>
        <h2 style={{ marginTop: 0 }}>Queue summary</h2>
        <div style={summaryGridStyle}>
          <SummaryCard title="Claims available" value={claimsQuery.data?.items.length ?? 0} />
          <SummaryCard title="Ready for review" value={queueItems.length} />
          <SummaryCard title="Escalation candidates" value={Math.min(queueItems.length, 3)} />
        </div>
      </section>

      <section style={panelStyle}>
        <h2 style={{ marginTop: 0 }}>Current queue</h2>
        {claimsQuery.isLoading && <p>Loading queue...</p>}
        {claimsQuery.isError && <p style={{ color: "crimson" }}>Failed to load queue.</p>}
        {claimsQuery.isSuccess && queueItems.length === 0 && <p>No review items yet.</p>}
        {claimsQuery.isSuccess && queueItems.length > 0 && (
          <ul style={{ marginBottom: 0 }}>
            {queueItems.map((item) => (
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

function SummaryCard({ title, value }: { title: string; value: number }) {
  return (
    <div style={summaryCardStyle}>
      <span style={{ color: "#666" }}>{title}</span>
      <strong style={{ fontSize: 28 }}>{value}</strong>
    </div>
  );
}

const panelStyle: React.CSSProperties = {
  border: "1px solid #ddd",
  borderRadius: 14,
  padding: 16,
  marginBottom: 20,
};

const summaryGridStyle: React.CSSProperties = {
  display: "grid",
  gridTemplateColumns: "repeat(auto-fit, minmax(180px, 1fr))",
  gap: 16,
};

const summaryCardStyle: React.CSSProperties = {
  border: "1px solid #eee",
  borderRadius: 12,
  padding: 16,
  display: "grid",
  gap: 8,
};
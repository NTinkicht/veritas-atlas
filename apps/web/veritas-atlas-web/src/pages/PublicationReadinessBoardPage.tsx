import { Link } from "react-router-dom";
import { useClaims } from "../hooks/useClaims";

export function PublicationReadinessBoardPage() {
  const claimsQuery = useClaims();
  const items = (claimsQuery.data?.items ?? []).slice(0, 10);

  return (
    <div style={{ padding: 24, fontFamily: "Arial, sans-serif" }}>
      <h1>Publication Readiness Board</h1>
      <p>Board view for items approaching publication after review and contradiction preparation.</p>

      <div style={{ display: "flex", gap: 12, flexWrap: "wrap", marginBottom: 20 }}>
        <Link to="/governance-console">Governance Console</Link>
        <Link to="/publication-desk">Publication Desk</Link>
        <Link to="/review-queue">Review Queue</Link>
      </div>

      <section style={gridStyle}>
        <BoardColumn title="Needs Review" items={items.slice(0, 3)} />
        <BoardColumn title="Needs Contradiction Check" items={items.slice(3, 6)} />
        <BoardColumn title="Ready to Publish" items={items.slice(6, 10)} />
      </section>
    </div>
  );
}

function BoardColumn({
  title,
  items,
}: {
  title: string;
  items: Array<{ id: string; topic: string; type: string; status: string }>;
}) {
  return (
    <div style={columnStyle}>
      <h3 style={{ marginTop: 0 }}>{title}</h3>
      {items.length === 0 && <p>No items.</p>}
      {items.length > 0 && (
        <ul style={{ marginBottom: 0 }}>
          {items.map((item) => (
            <li key={item.id}>
              <Link to={`/claims/${item.id}`}>{item.topic}</Link> - {item.type} - {item.status}
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}

const gridStyle: React.CSSProperties = {
  display: "grid",
  gridTemplateColumns: "repeat(auto-fit, minmax(260px, 1fr))",
  gap: 16,
};

const columnStyle: React.CSSProperties = {
  border: "1px solid #ddd",
  borderRadius: 14,
  padding: 16,
  minHeight: 220,
};
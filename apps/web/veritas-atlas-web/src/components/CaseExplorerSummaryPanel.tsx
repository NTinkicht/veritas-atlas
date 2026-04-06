export function CaseExplorerSummaryPanel({
  totalCases,
  openCases,
  totalClaims,
  totalContradictions,
}: {
  totalCases: number;
  openCases: number;
  totalClaims: number;
  totalContradictions: number;
}) {
  return (
    <div style={gridStyle}>
      <Card title="Cases" value={totalCases} />
      <Card title="Open Cases" value={openCases} />
      <Card title="Claims" value={totalClaims} />
      <Card title="Contradictions" value={totalContradictions} />
    </div>
  );
}

function Card({ title, value }: { title: string; value: number }) {
  return (
    <div style={cardStyle}>
      <span style={{ color: "#666" }}>{title}</span>
      <strong style={{ fontSize: 28 }}>{value}</strong>
    </div>
  );
}

const gridStyle: React.CSSProperties = {
  display: "grid",
  gridTemplateColumns: "repeat(auto-fit, minmax(180px, 1fr))",
  gap: 16,
};

const cardStyle: React.CSSProperties = {
  border: "1px solid #ddd",
  borderRadius: 14,
  padding: 16,
  display: "grid",
  gap: 8,
};
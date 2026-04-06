type PortfolioMetric = {
  label: string;
  value: number;
};

export function PortfolioRollupPanel({
  metrics,
}: {
  metrics: PortfolioMetric[];
}) {
  return (
    <div style={panelStyle}>
      <h3 style={{ marginTop: 0 }}>Portfolio Rollup</h3>
      <div style={gridStyle}>
        {metrics.map((metric) => (
          <div key={metric.label} style={cardStyle}>
            <span style={{ color: "#666" }}>{metric.label}</span>
            <strong style={{ fontSize: 28 }}>{metric.value}</strong>
          </div>
        ))}
      </div>
    </div>
  );
}

const panelStyle: React.CSSProperties = {
  border: "1px solid #ddd",
  borderRadius: 14,
  padding: 16,
};

const gridStyle: React.CSSProperties = {
  display: "grid",
  gridTemplateColumns: "repeat(auto-fit, minmax(160px, 1fr))",
  gap: 12,
};

const cardStyle: React.CSSProperties = {
  border: "1px solid #eee",
  borderRadius: 12,
  padding: 12,
  display: "grid",
  gap: 8,
};
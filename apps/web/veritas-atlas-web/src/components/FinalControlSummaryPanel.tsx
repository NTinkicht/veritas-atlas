type FinalControlMetric = {
  label: string;
  value: string;
};

export function FinalControlSummaryPanel({
  metrics,
}: {
  metrics: FinalControlMetric[];
}) {
  return (
    <div style={panelStyle}>
      <h3 style={{ marginTop: 0 }}>Final Control Summary</h3>
      <ul style={{ marginBottom: 0 }}>
        {metrics.map((metric) => (
          <li key={metric.label}>
            <strong>{metric.label}</strong>: {metric.value}
          </li>
        ))}
      </ul>
    </div>
  );
}

const panelStyle: React.CSSProperties = {
  border: "1px solid #ddd",
  borderRadius: 14,
  padding: 16,
};
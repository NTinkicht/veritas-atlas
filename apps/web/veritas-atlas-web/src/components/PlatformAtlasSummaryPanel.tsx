type AtlasMetric = {
  label: string;
  value: string;
};

export function PlatformAtlasSummaryPanel({
  metrics,
}: {
  metrics: AtlasMetric[];
}) {
  return (
    <div style={panelStyle}>
      <h3 style={{ marginTop: 0 }}>Platform Atlas Summary</h3>
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
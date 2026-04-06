type TruthMatrixItem = {
  id: string;
  label: string;
  status: string;
};

export function TruthMatrixPanel({
  claims,
  contradictions,
}: {
  claims: TruthMatrixItem[];
  contradictions: TruthMatrixItem[];
}) {
  return (
    <div style={panelStyle}>
      <h3 style={{ marginTop: 0 }}>Truth Matrix</h3>

      <div style={gridStyle}>
        <div>
          <h4>Claims</h4>
          {claims.length === 0 && <p>No claims.</p>}
          {claims.length > 0 && (
            <ul style={{ marginBottom: 0 }}>
              {claims.map((item) => (
                <li key={item.id}>{item.label} - {item.status}</li>
              ))}
            </ul>
          )}
        </div>

        <div>
          <h4>Contradictions</h4>
          {contradictions.length === 0 && <p>No contradictions.</p>}
          {contradictions.length > 0 && (
            <ul style={{ marginBottom: 0 }}>
              {contradictions.map((item) => (
                <li key={item.id}>{item.label} - {item.status}</li>
              ))}
            </ul>
          )}
        </div>
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
  gridTemplateColumns: "1fr 1fr",
  gap: 16,
};
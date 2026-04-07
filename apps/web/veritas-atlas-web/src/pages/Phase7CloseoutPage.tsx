export function Phase7CloseoutPage() {
  const items = [
    "Smoke and workflow diagnostics added",
    "Write action endpoints added",
    "Review workflow endpoints added",
    "Publication workflow endpoints added",
    "Frontend action and workflow mutation wiring added",
    "Workflow console and mutation playground added",
    "Phase 7 ready to hand off into deeper persistence and business logic"
  ];

  return (
    <div style={{ fontFamily: "Arial, sans-serif", padding: 24 }}>
      <h1 style={{ marginTop: 0 }}>Phase 7 Closeout</h1>
      <p style={{ color: "#555" }}>
        Closeout summary for the entire Phase 7 depth track.
      </p>

      <div style={panelStyle}>
        <ul style={{ marginBottom: 0 }}>
          {items.map((item) => (
            <li key={item}>{item}</li>
          ))}
        </ul>
      </div>
    </div>
  );
}

const panelStyle: React.CSSProperties = {
  border: "1px solid #ddd",
  borderRadius: 14,
  padding: 16,
};
import { Link } from "react-router-dom";

export function DecisionLogPage() {
  const decisions = [
    "Review queue policy placeholder",
    "Publication routing placeholder",
    "Escalation rule placeholder",
    "Contradiction severity placeholder",
  ];

  return (
    <div style={{ padding: 24, fontFamily: "Arial, sans-serif" }}>
      <h1>Decision Log</h1>
      <p>Central place for governance and publication decisions.</p>

      <div style={{ display: "flex", gap: 12, flexWrap: "wrap", marginBottom: 20 }}>
        <Link to="/governance-console">Governance Console</Link>
        <Link to="/publication-readiness">Publication Readiness</Link>
        <Link to="/publication-desk">Publication Desk</Link>
      </div>

      <section style={panelStyle}>
        <h2 style={{ marginTop: 0 }}>Current decisions</h2>
        <ul style={{ marginBottom: 0 }}>
          {decisions.map((item, index) => (
            <li key={index}>{item}</li>
          ))}
        </ul>
      </section>
    </div>
  );
}

const panelStyle: React.CSSProperties = {
  border: "1px solid #ddd",
  borderRadius: 14,
  padding: 16,
};
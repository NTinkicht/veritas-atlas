import { Link } from "react-router-dom";

export function QualityRadarPage() {
  const dimensions = [
    "Evidence quality",
    "Statement clarity",
    "Claim quality",
    "Contradiction readiness",
    "Review traceability",
    "Publication readiness"
  ];

  return (
    <div style={{ padding: 24, fontFamily: "Arial, sans-serif" }}>
      <h1>Quality Radar</h1>
      <p>Operational quality dimensions for reviewing system maturity and readiness.</p>

      <div style={{ display: "flex", gap: 12, flexWrap: "wrap", marginBottom: 20 }}>
        <Link to="/analytics-center">Analytics Center</Link>
        <Link to="/governance-console">Governance Console</Link>
        <Link to="/publication-readiness">Publication Readiness</Link>
      </div>

      <section style={panelStyle}>
        <ul style={{ marginBottom: 0 }}>
          {dimensions.map((dimension) => (
            <li key={dimension}>{dimension} - placeholder</li>
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
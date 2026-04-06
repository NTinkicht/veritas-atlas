import { Link } from "react-router-dom";

export function CaseFlowMapPage() {
  const steps = [
    "Source registration",
    "Document intake",
    "Evidence extraction",
    "Statement creation",
    "Claim formulation",
    "Contradiction preparation",
    "Review routing",
    "Publication readiness",
  ];

  return (
    <div style={{ padding: 24, fontFamily: "Arial, sans-serif" }}>
      <h1>Case Flow Map</h1>
      <p>Visual sequence of how information moves through the Veritas Atlas operational system.</p>

      <div style={{ display: "flex", gap: 12, flexWrap: "wrap", marginBottom: 20 }}>
        <Link to="/analytics-center">Analytics Center</Link>
        <Link to="/operations">Operations Hub</Link>
        <Link to="/review-queue">Review Queue</Link>
      </div>

      <section style={panelStyle}>
        <ol style={{ marginBottom: 0, paddingLeft: 20 }}>
          {steps.map((step) => (
            <li key={step} style={{ marginBottom: 8 }}>{step}</li>
          ))}
        </ol>
      </section>
    </div>
  );
}

const panelStyle: React.CSSProperties = {
  border: "1px solid #ddd",
  borderRadius: 14,
  padding: 16,
};
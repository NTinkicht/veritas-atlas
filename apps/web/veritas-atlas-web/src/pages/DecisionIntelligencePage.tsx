import { ConfidencePanel } from "../components/ConfidencePanel";
import { DecisionExplainerPanel } from "../components/DecisionExplainerPanel";

export function DecisionIntelligencePage() {
  return (
    <div style={{ padding: 24 }}>
      <h1>Decision Intelligence</h1>

      <div style={{ display: "grid", gap: 16, gridTemplateColumns: "1fr 1fr" }}>
        <ConfidencePanel score={82} />
        <DecisionExplainerPanel />
      </div>
    </div>
  );
}
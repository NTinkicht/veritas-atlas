import { Link } from "react-router-dom";

export function InvestigationNavigatorPage() {
  return (
    <div style={{ padding: 24 }}>
      <h1>Investigation Navigator</h1>
      <p>Unified navigation across Cases, Claims, Statements, and Contradictions.</p>

      <div style={{ display: "flex", gap: 12, flexWrap: "wrap", marginBottom: 20 }}>
        <Link to="/operations-intelligence">Operations Intelligence</Link>
        <Link to="/operations">Operations Hub</Link>
        <Link to="/statements">Statements</Link>
        <Link to="/claims">Claims</Link>
        <Link to="/contradictions/workspace">Contradictions Workspace</Link>
      </div>

      <section style={{ marginBottom: 24 }}>
        <h2>Quick Access</h2>
        <ul>
          <li>Latest Claims</li>
          <li>Latest Statements</li>
          <li>Recent Contradictions</li>
        </ul>
      </section>

      <section>
        <h2>Cross-Linking</h2>
        <p>Jump between related entities to follow the investigation graph.</p>
      </section>
    </div>
  );
}
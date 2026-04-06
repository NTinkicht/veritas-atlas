import { Link } from "react-router-dom";
import { useClaims } from "../hooks/useClaims";
import { useContradictions } from "../hooks/useContradictions";
import { ExecutiveReadoutPanel } from "../components/ExecutiveReadoutPanel";

export function ExecutiveReadoutWorkspacePage() {
  const claimsQuery = useClaims();
  const contradictionsQuery = useContradictions();

  const items = [
    { label: "Claims visible", value: String(claimsQuery.data?.items.length ?? 0) },
    { label: "Contradictions visible", value: String(contradictionsQuery.data?.items.length ?? 0) },
    { label: "Delivery state", value: "Operational prototype" },
    { label: "Readiness mode", value: "Expansion + stabilization" },
  ];

  return (
    <div style={{ fontFamily: "Arial, sans-serif", padding: 24 }}>
      <header style={{ marginBottom: 24 }}>
        <h1 style={{ margin: 0 }}>Executive Readout Workspace</h1>
        <p style={{ color: "#555" }}>
          High-level rollup across the current Veritas Atlas operational system.
        </p>
        <nav style={{ display: "flex", gap: 16, marginTop: 12, flexWrap: "wrap" }}>
          <Link to="/">Home</Link>
          <Link to="/executive-readout-workspace">Executive Readout</Link>
          <Link to="/release-readiness-hub">Release Readiness Hub</Link>
          <Link to="/ops-coordination-center">Ops Coordination</Link>
        </nav>
      </header>

      <ExecutiveReadoutPanel items={items} />
    </div>
  );
}
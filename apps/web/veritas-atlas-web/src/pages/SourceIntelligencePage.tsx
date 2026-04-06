import { Link } from "react-router-dom";
import { useSources } from "../hooks/useSources";
import { SourceIntelligencePanel } from "../components/SourceIntelligencePanel";

export function SourceIntelligencePage() {
  const sourcesQuery = useSources();

  const items = (sourcesQuery.data?.items ?? []).slice(0, 12).map((item) => ({
    id: item.id,
    name: item.name,
    status: item.status,
  }));

  return (
    <div style={{ fontFamily: "Arial, sans-serif", padding: 24 }}>
      <header style={{ marginBottom: 24 }}>
        <h1 style={{ margin: 0 }}>Source Intelligence</h1>
        <p style={{ color: "#555" }}>
          Source-focused operational view for ingestion quality and source coverage.
        </p>
        <nav style={{ display: "flex", gap: 16, marginTop: 12, flexWrap: "wrap" }}>
          <Link to="/sources">Sources</Link>
          <Link to="/source-intelligence">Source Intelligence</Link>
          <Link to="/evidence-trace">Evidence Trace</Link>
        </nav>
      </header>

      <SourceIntelligencePanel items={items} />
    </div>
  );
}
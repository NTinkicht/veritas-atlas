import { Link } from "react-router-dom";
import { RouteRegistryPanel } from "../components/RouteRegistryPanel";

export function RouteRegistryPage() {
  const items = [
    { category: "Core entity pages", count: 12 },
    { category: "Review and contradiction pages", count: 10 },
    { category: "Publication and governance pages", count: 10 },
    { category: "Executive and readiness pages", count: 10 },
  ];

  return (
    <div style={{ fontFamily: "Arial, sans-serif", padding: 24 }}>
      <header style={{ marginBottom: 24 }}>
        <h1 style={{ margin: 0 }}>Route Registry</h1>
        <p style={{ color: "#555" }}>
          Registry-style summary of the current operational route families inside the frontend.
        </p>
        <nav style={{ display: "flex", gap: 16, marginTop: 12, flexWrap: "wrap" }}>
          <Link to="/route-registry">Route Registry</Link>
          <Link to="/platform-atlas">Platform Atlas</Link>
          <Link to="/workspace-map">Workspace Map</Link>
        </nav>
      </header>

      <RouteRegistryPanel items={items} />
    </div>
  );
}
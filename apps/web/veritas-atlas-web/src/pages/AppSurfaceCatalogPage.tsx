import { AppSurfaceCatalogPanel } from "../components/AppSurfaceCatalogPanel";

export function AppSurfaceCatalogPage() {
  const items = [
    { label: "Case Explorer", route: "/case-explorer", description: "Primary case navigation and workbench entry." },
    { label: "Truth Review Studio", route: "/truth-review-studio", description: "Claims and contradictions review surface." },
    { label: "Publication Pipeline", route: "/publication-pipeline", description: "Publication preparation and routing shell." },
    { label: "Decision Intelligence", route: "/decision-intelligence", description: "Confidence and decision explanation surface." },
    { label: "Release Readiness Hub", route: "/release-readiness-hub", description: "Release and readiness overview." },
    { label: "Operator Cockpit", route: "/operator-cockpit", description: "Central operator jump-off surface." },
    { label: "Platform Atlas", route: "/platform-atlas", description: "Platform-level orientation and state summary." },
    { label: "Workspace Map", route: "/workspace-map", description: "Route-level map of major workspaces." },
  ];

  return (
    <div style={{ fontFamily: "Arial, sans-serif", padding: 24 }}>
      <h1 style={{ marginTop: 0 }}>App Surface Catalog</h1>
      <p style={{ color: "#555" }}>
        Final catalog of the major frontend surfaces now present in Veritas Atlas.
      </p>
      <AppSurfaceCatalogPanel items={items} />
    </div>
  );
}
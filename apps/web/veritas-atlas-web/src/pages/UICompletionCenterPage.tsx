import { CompletionChecklistPanel } from "../components/CompletionChecklistPanel";
import { ReferenceLinksPanel } from "../components/ReferenceLinksPanel";

export function UICompletionCenterPage() {
  const checklist = [
    { label: "Core entity pages", status: "Complete" },
    { label: "Case explorer surfaces", status: "Complete" },
    { label: "Contradiction and review surfaces", status: "Complete" },
    { label: "Publication and governance shells", status: "Complete" },
    { label: "Readiness and executive surfaces", status: "Complete" },
    { label: "Deep business logic wiring", status: "Pending deeper pass" },
  ];

  const links = [
    { label: "App Surface Catalog", route: "/app-surface-catalog" },
    { label: "Release Readiness Hub", route: "/release-readiness-hub" },
    { label: "System Readiness Map", route: "/system-readiness-map" },
    { label: "Final Control Center", route: "/final-control-center" },
  ];

  return (
    <div style={{ fontFamily: "Arial, sans-serif", padding: 24 }}>
      <h1 style={{ marginTop: 0 }}>UI Completion Center</h1>
      <p style={{ color: "#555" }}>
        Final consolidation view for the completed frontend shell and the remaining deeper implementation work.
      </p>

      <div style={gridStyle}>
        <CompletionChecklistPanel items={checklist} />
        <ReferenceLinksPanel items={links} />
      </div>
    </div>
  );
}

const gridStyle: React.CSSProperties = {
  display: "grid",
  gridTemplateColumns: "1fr 1fr",
  gap: 16,
};
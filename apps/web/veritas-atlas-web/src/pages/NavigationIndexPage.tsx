import { Link } from "react-router-dom";

export function NavigationIndexPage() {
  const groups = [
    {
      title: "Core Work",
      items: [
        { label: "Cases", route: "/cases" },
        { label: "Case Explorer", route: "/case-explorer" },
        { label: "Claims", route: "/claims" },
        { label: "Contradictions", route: "/contradictions" },
      ],
    },
    {
      title: "Review and Publication",
      items: [
        { label: "Truth Review Studio", route: "/truth-review-studio" },
        { label: "Review Decision Board", route: "/review-decision-board" },
        { label: "Publication Pipeline", route: "/publication-pipeline" },
        { label: "Publication Governance", route: "/publication-governance" },
      ],
    },
    {
      title: "Oversight and Readiness",
      items: [
        { label: "Decision Intelligence", route: "/decision-intelligence" },
        { label: "Release Readiness Hub", route: "/release-readiness-hub" },
        { label: "System Readiness Map", route: "/system-readiness-map" },
        { label: "Executive Readout", route: "/executive-readout-workspace" },
      ],
    },
  ];

  return (
    <div style={{ fontFamily: "Arial, sans-serif", padding: 24 }}>
      <h1 style={{ marginTop: 0 }}>Navigation Index</h1>
      <p style={{ color: "#555" }}>
        Organized route index across the current Veritas Atlas frontend.
      </p>

      <div style={gridStyle}>
        {groups.map((group) => (
          <div key={group.title} style={panelStyle}>
            <h3 style={{ marginTop: 0 }}>{group.title}</h3>
            <ul style={{ marginBottom: 0 }}>
              {group.items.map((item) => (
                <li key={item.route}>
                  <Link to={item.route}>{item.label}</Link>
                </li>
              ))}
            </ul>
          </div>
        ))}
      </div>
    </div>
  );
}

const gridStyle: React.CSSProperties = {
  display: "grid",
  gridTemplateColumns: "repeat(auto-fit, minmax(260px, 1fr))",
  gap: 16,
};

const panelStyle: React.CSSProperties = {
  border: "1px solid #ddd",
  borderRadius: 14,
  padding: 16,
};
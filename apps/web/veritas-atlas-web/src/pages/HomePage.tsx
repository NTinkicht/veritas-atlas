import { Link } from "react-router-dom";
import { AppSurface } from "../components/AppSurface";

const cards = [
  {
    href: "/dashboard",
    title: "Operations Intelligence",
    text: "A live overview of cases, claims, contradictions, workflow activity, and snapshot state."
  },
  {
    href: "/cases",
    title: "Case Registry",
    text: "Browse, inspect, and organize case records with a cleaner operational structure."
  },
  {
    href: "/claims",
    title: "Claims Workspace",
    text: "Track normalized claims and their linked statements through a sharper, more readable UI."
  },
  {
    href: "/contradictions",
    title: "Contradiction Console",
    text: "Inspect contradictions, severity, and status in a more focused review surface."
  },
  {
    href: "/workflow-audit",
    title: "Workflow Audit",
    text: "Review transitions and decisions in a timeline that feels trustworthy and production-ready."
  },
  {
    href: "/persistence",
    title: "Persistence Console",
    text: "Control seeded lifecycle state, refresh snapshots, and reset safely."
  }
];

export function HomePage() {
  return (
    <AppSurface
      title="Truth intelligence, redesigned."
      subtitle="Veritas Atlas now has a premium command-platform shell, stronger navigation, and cleaner operational surfaces for the pages that matter most."
    >
      <section className="grid-2">
        <div className="panel">
          <div className="panel-header">
            <h2 className="panel-title">Why this matters</h2>
          </div>
          <div className="kv-grid">
            <div className="kv-row"><div className="kv-label">Clearer command view</div><div className="kv-value">The dashboard, list pages, and persistence console are now readable, structured, and presentation-ready.</div></div>
            <div className="kv-row"><div className="kv-label">Consistent navigation</div><div className="kv-value">All primary pages now share the same sidebar, theme, spacing, and information hierarchy.</div></div>
            <div className="kv-row"><div className="kv-label">Production direction</div><div className="kv-value">This is now a real web product shell, not just a raw debug interface.</div></div>
          </div>
        </div>

        <div className="panel">
          <div className="panel-header">
            <h2 className="panel-title">Start here</h2>
          </div>
          <div className="action-row">
            <Link to="/dashboard"><button>Open Dashboard</button></Link>
            <Link to="/login"><button>Open Login</button></Link>
          </div>
          <p className="hero-subtitle" style={{ marginTop: 18 }}>
            Use the navigation on the left to move between the restored operational pages.
          </p>
        </div>
      </section>

      <section className="link-grid">
        {cards.map((card) => (
          <Link key={card.href} to={card.href} className="link-card">
            <strong>{card.title}</strong>
            <span>{card.text}</span>
          </Link>
        ))}
      </section>
    </AppSurface>
  );
}
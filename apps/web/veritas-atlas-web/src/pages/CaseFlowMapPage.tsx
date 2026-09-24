import { useEffect, useState } from "react";
import { Link } from "react-router-dom";
import { getCases } from "../api/cases";
import type { CaseItem } from "../api/contracts";
import { AppSurface } from "../components/AppSurface";
import { useFrontendNav } from "../hooks/useFrontendNav";

export function CaseFlowMapPage() {
  const links = useFrontendNav();
  const [items, setItems] = useState<CaseItem[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  async function load() {
    setLoading(true);
    setError(null);
    try {
      const result = await getCases(1, 100);
      setItems(result.items);
    } catch (err) {
      setError(
        err instanceof Error ? err.message : "Failed to load case flow summary.",
      );
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => {
    void load();
  }, []);

  const stages = ["Open", "Approved", "Rejected"].map((status) => ({
    status,
    count: items.filter(
      (item) => item.status.toLowerCase() === status.toLowerCase(),
    ).length,
  }));

  return (
    <AppSurface
      title="Case Flow Map"
      subtitle="Read-only view of how cases are distributed across workflow states."
      links={links}
    >
      <div className="action-row">
        <button onClick={() => void load()} disabled={loading}>
          Refresh
        </button>
        <Link to="/cases">Open Case Explorer</Link>
      </div>

      {error ? (
        <div className="notice-card notice-danger" role="alert">
          {error}
        </div>
      ) : null}
      {loading ? (
        <div className="notice-card" role="status" aria-live="polite">
          Loading case flow...
        </div>
      ) : null}

      {!loading && !error ? (
        <section className="panel" aria-labelledby="case-flow-heading">
          <h2 id="case-flow-heading" className="panel-title">
            Current workflow distribution
          </h2>
          <dl className="kv-grid" aria-live="polite">
            {stages.map((stage) => (
              <div className="kv-row" key={stage.status}>
                <dt className="kv-label">{stage.status}</dt>
                <dd className="kv-value">{stage.count}</dd>
              </div>
            ))}
          </dl>
          <p>{items.length} cases represented in this read-only snapshot.</p>
        </section>
      ) : null}
    </AppSurface>
  );
}

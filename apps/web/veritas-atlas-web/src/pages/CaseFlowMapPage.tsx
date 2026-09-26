import { useEffect, useState } from "react";
import { Link } from "react-router-dom";
import { getCases } from "../api/cases";
import type { CaseItem } from "../api/contracts";
import { AppSurface } from "../components/AppSurface";
import { useFrontendNav } from "../hooks/useFrontendNav";

const CASE_STATUS_ORDER = [
  "Open",
  "InReview",
  "OnHold",
  "Approved",
  "Rejected",
  "Published",
  "Closed",
];

export function CaseFlowMapPage() {
  const links = useFrontendNav();
  const [items, setItems] = useState<CaseItem[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  async function load() {
    setLoading(true);
    setError(null);
    try {
      const first = await getCases(1, 100);
      const allItems = [...first.items];

      for (let page = 2; page <= first.totalPages; page += 1) {
        const next = await getCases(page, 100);
        allItems.push(...next.items);
      }

      setItems(allItems);
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

  const counts = new Map<string, number>();
  for (const item of items) {
    counts.set(item.status, (counts.get(item.status) ?? 0) + 1);
  }

  const known = CASE_STATUS_ORDER.map((status) => ({
    status,
    count: counts.get(status) ?? 0,
  }));
  const knownKeys = new Set(CASE_STATUS_ORDER.map((status) => status.toLowerCase()));
  const unexpected = Array.from(counts.entries())
    .filter(([status]) => !knownKeys.has(status.toLowerCase()))
    .sort(([left], [right]) => left.localeCompare(right))
    .map(([status, count]) => ({ status, count }));
  const stages = [...known, ...unexpected];

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

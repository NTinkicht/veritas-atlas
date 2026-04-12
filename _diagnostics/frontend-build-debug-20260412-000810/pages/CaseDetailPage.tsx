import { useEffect, useState } from "react";
import { Link, useParams } from "react-router-dom";
import { getCaseById } from "../api/cases";
import type { CaseItem } from "../api/contracts";
import { AppSurface } from "../components/AppSurface";
import { WorkflowActionPanel } from "../components/WorkflowActionPanel";

export function CaseDetailPage() {
  const { id = "" } = useParams();
  const [item, setItem] = useState<CaseItem | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  async function load() {
    setLoading(true);
    setError(null);

    try {
      const result = await getCaseById(id);
      setItem(result);
    } catch (err) {
      const message =
        typeof err === "object" && err && "message" in err
          ? String((err as { message?: unknown }).message ?? "Failed to load case.")
          : "Failed to load case.";
      setError(message);
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => {
    if (id) {
      void load();
    }
  }, [id]);

  return (
    <AppSurface
      title="Case Detail"
      subtitle="Detailed view of a single case record."
    >
      <div className="action-row">
        <Link to="/cases"><button>Back to Cases</button></Link>
        <button onClick={() => void load()} disabled={loading}>Refresh</button>
      </div>

      {error ? <div className="notice-card notice-danger">{error}</div> : null}
      {loading ? <div className="notice-card">Loading case...</div> : null}

      {!loading && item ? (
        <>
          <section className="panel">
            <div className="panel-header">
              <h2 className="panel-title">{item.title}</h2>
              <span className="tag">{item.status}</span>
            </div>

            <div className="kv-grid">
              <div className="kv-row"><div className="kv-label">Id</div><div className="kv-value">{item.id}</div></div>
              <div className="kv-row"><div className="kv-label">Title</div><div className="kv-value">{item.title}</div></div>
              <div className="kv-row"><div className="kv-label">Status</div><div className="kv-value">{item.status}</div></div>
              <div className="kv-row"><div className="kv-label">Created At</div><div className="kv-value">{new Date(item.createdAtUtc).toLocaleString()}</div></div>
              <div className="kv-row"><div className="kv-label">Updated At</div><div className="kv-value">{new Date(item.updatedAtUtc).toLocaleString()}</div></div>
            </div>
          </section>

          <WorkflowActionPanel caseId={item.id} caseStatus={item.status} onRefresh={load} />
        </>
      ) : null}
    </AppSurface>
  );
}
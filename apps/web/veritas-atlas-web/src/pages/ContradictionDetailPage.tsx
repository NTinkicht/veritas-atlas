import { useEffect, useState } from "react";
import { Link, useParams } from "react-router-dom";
import { getContradictionById } from "../api/contradictions";
import type { ContradictionItem } from "../api/contracts";
import { AppSurface } from "../components/AppSurface";

export function ContradictionDetailPage() {
  const { id = "" } = useParams();
  const [item, setItem] = useState<ContradictionItem | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    async function load() {
      setLoading(true);
      setError(null);

      try {
        const result = await getContradictionById(id);
        setItem(result);
      } catch (err) {
        const message =
          typeof err === "object" && err && "message" in err
            ? String((err as { message?: unknown }).message ?? "Failed to load contradiction.")
            : "Failed to load contradiction.";
        setError(message);
      } finally {
        setLoading(false);
      }
    }

    if (id) {
      void load();
    }
  }, [id]);

  return (
    <AppSurface
      title="Contradiction Detail"
      subtitle="Detailed view of a single contradiction record."
    >
      <div className="action-row">
        <Link to="/contradictions"><button>Back to Contradictions</button></Link>
      </div>

      {error ? <div className="notice-card notice-danger">{error}</div> : null}
      {loading ? <div className="notice-card">Loading contradiction...</div> : null}

      {!loading && item ? (
        <section className="panel">
          <div className="panel-header">
            <h2 className="panel-title">{item.summary}</h2>
            <span className="tag">{item.status}</span>
          </div>

          <div className="kv-grid">
            <div className="kv-row"><div className="kv-label">Id</div><div className="kv-value">{item.id}</div></div>
            <div className="kv-row"><div className="kv-label">Summary</div><div className="kv-value">{item.summary}</div></div>
            <div className="kv-row"><div className="kv-label">Type</div><div className="kv-value">{item.type}</div></div>
            <div className="kv-row"><div className="kv-label">Severity</div><div className="kv-value">{item.severity}</div></div>
            <div className="kv-row"><div className="kv-label">Status</div><div className="kv-value">{item.status}</div></div>
            <div className="kv-row"><div className="kv-label">Case Id</div><div className="kv-value">{item.caseId}</div></div>
            <div className="kv-row"><div className="kv-label">Left Claim Id</div><div className="kv-value">{item.leftClaimId}</div></div>
            <div className="kv-row"><div className="kv-label">Right Claim Id</div><div className="kv-value">{item.rightClaimId}</div></div>
            <div className="kv-row"><div className="kv-label">Rationale</div><div className="kv-value">{item.rationale ?? "N/A"}</div></div>
          </div>
        </section>
      ) : null}
    </AppSurface>
  );
}
import { useEffect, useState } from "react";
import { Link, useParams } from "react-router-dom";
import { getClaimById } from "../api/claims";
import type { ClaimItem } from "../api/contracts";
import { AppSurface } from "../components/AppSurface";

export function ClaimDetailPage() {
  const { id = "" } = useParams();
  const [item, setItem] = useState<ClaimItem | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    async function load() {
      setLoading(true);
      setError(null);

      try {
        const result = await getClaimById(id);
        setItem(result);
      } catch (err) {
        const message =
          typeof err === "object" && err && "message" in err
            ? String((err as { message?: unknown }).message ?? "Failed to load claim.")
            : "Failed to load claim.";
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
      title="Claim Detail"
      subtitle="Detailed view of a single claim record."
    >
      <div className="action-row">
        <Link to="/claims"><button>Back to Claims</button></Link>
      </div>

      {error ? <div className="notice-card notice-danger">{error}</div> : null}
      {loading ? <div className="notice-card">Loading claim...</div> : null}

      {!loading && item ? (
        <section className="panel">
          <div className="panel-header">
            <h2 className="panel-title">{item.topic}</h2>
            <span className="tag">{item.status}</span>
          </div>

          <div className="kv-grid">
            <div className="kv-row"><div className="kv-label">Id</div><div className="kv-value">{item.id}</div></div>
            <div className="kv-row"><div className="kv-label">Topic</div><div className="kv-value">{item.topic}</div></div>
            <div className="kv-row"><div className="kv-label">Type</div><div className="kv-value">{item.type}</div></div>
            <div className="kv-row"><div className="kv-label">Status</div><div className="kv-value">{item.status}</div></div>
            <div className="kv-row"><div className="kv-label">Statement Id</div><div className="kv-value">{item.statementId}</div></div>
            <div className="kv-row"><div className="kv-label">Case Id</div><div className="kv-value">{item.caseId ?? "N/A"}</div></div>
            <div className="kv-row"><div className="kv-label">Person Id</div><div className="kv-value">{item.personId ?? "N/A"}</div></div>
          </div>
        </section>
      ) : null}
    </AppSurface>
  );
}
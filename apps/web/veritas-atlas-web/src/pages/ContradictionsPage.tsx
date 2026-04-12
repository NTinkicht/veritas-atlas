import { useEffect, useState } from "react";
import { Link, useNavigate } from "react-router-dom";
import { getContradictions } from "../api/contradictions";
import type { ContradictionItem, PagedResponse } from "../api/contracts";
import { AppSurface } from "../components/AppSurface";
import { LoadingState } from "../components/LoadingState";
import { ErrorState } from "../components/ErrorState";
import { EmptyState } from "../components/EmptyState";

function statusTag(value: string) {
  return <span className="tag">{value}</span>;
}

export function ContradictionsPage() {
  const navigate = useNavigate();
  const [page, setPage] = useState(1);
  const [data, setData] = useState<PagedResponse<ContradictionItem> | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  async function load() {
    setLoading(true);
    setError(null);

    try {
      const result = await getContradictions(page, 20);
      setData(result);
    } catch (err) {
      const message =
        typeof err === "object" && err && "message" in err
          ? String((err as { message?: unknown }).message ?? "Failed to load contradictions.")
          : "Failed to load contradictions.";
      setError(message);
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => {
    void load();
  }, [page]);

  return (
    <AppSurface
      title="Contradictions"
      subtitle="Track contradiction records, their severity, and their current resolution state."
    >
      <div className="action-row">
        <button onClick={() => void load()} disabled={loading}>Refresh</button>
      </div>

      {error ? <ErrorState message={error} /> : null}
      {loading ? <LoadingState message="Loading contradictions..." /> : null}

      {!loading && data ? (
        <section className="panel">
          <div className="panel-header">
            <h2 className="panel-title">Contradiction Registry</h2>
            <span className="tag">{data.totalCount} total</span>
          </div>

          {(data.items?.length ?? 0) === 0 ? (
            <EmptyState message="No contradiction records available." />
          ) : (
            <div className="table-wrap">
              <table className="table-card">
                <thead>
                  <tr>
                    <th>Summary</th>
                    <th>Type</th>
                    <th>Severity</th>
                    <th>Status</th>
                  </tr>
                </thead>
                <tbody>
                  {data.items.map((item) => (
                    <tr
                      key={item.id}
                      onClick={() => navigate(`/contradictions/${item.id}`)}
                      style={{ cursor: "pointer" }}
                    >
                      <td><Link to={`/contradictions/${item.id}`} onClick={(e) => e.stopPropagation()}>{item.summary}</Link></td>
                      <td>{item.type}</td>
                      <td>{item.severity}</td>
                      <td>{statusTag(item.status)}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}

          <div className="action-row" style={{ marginTop: 16 }}>
            <button onClick={() => setPage((p) => Math.max(1, p - 1))} disabled={page <= 1}>
              Previous
            </button>
            <div className="tag">Page {data.page} / {Math.max(1, data.totalPages)}</div>
            <button onClick={() => setPage((p) => (data.totalPages > p ? p + 1 : p))} disabled={page >= data.totalPages}>
              Next
            </button>
          </div>
        </section>
      ) : null}
    </AppSurface>
  );
}
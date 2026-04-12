import { useEffect, useState } from "react";
import { Link, useNavigate } from "react-router-dom";
import { getCases } from "../api/cases";
import type { CaseItem, PagedResponse } from "../api/contracts";
import { AppSurface } from "../components/AppSurface";
import { LoadingState } from "../components/LoadingState";
import { ErrorState } from "../components/ErrorState";
import { EmptyState } from "../components/EmptyState";

function statusTag(value: string) {
  return <span className="tag">{value}</span>;
}

export function CasesPage() {
  const navigate = useNavigate();
  const [page, setPage] = useState(1);
  const [data, setData] = useState<PagedResponse<CaseItem> | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  async function load() {
    setLoading(true);
    setError(null);

    try {
      const result = await getCases(page, 20);
      setData(result);
    } catch (err) {
      const message =
        typeof err === "object" && err && "message" in err
          ? String((err as { message?: unknown }).message ?? "Failed to load cases.")
          : "Failed to load cases.";
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
      title="Cases"
      subtitle="Browse live case records with status and timeline information."
    >
      <div className="action-row">
        <button onClick={() => void load()} disabled={loading}>Refresh</button>
      </div>

      {error ? <ErrorState message={error} /> : null}
      {loading ? <LoadingState message="Loading cases..." /> : null}

      {!loading && data ? (
        <section className="panel">
          <div className="panel-header">
            <h2 className="panel-title">Case Registry</h2>
            <span className="tag">{data.totalCount} total</span>
          </div>

          {(data.items?.length ?? 0) === 0 ? (
            <EmptyState message="No case records available." />
          ) : (
            <div className="table-wrap">
              <table className="table-card">
                <thead>
                  <tr>
                    <th>Title</th>
                    <th>Status</th>
                    <th>Created</th>
                    <th>Updated</th>
                  </tr>
                </thead>
                <tbody>
                  {data.items.map((item) => (
                    <tr
                      key={item.id}
                      onClick={() => navigate(`/cases/${item.id}`)}
                      style={{ cursor: "pointer" }}
                    >
                      <td><Link to={`/cases/${item.id}`} onClick={(e) => e.stopPropagation()}>{item.title}</Link></td>
                      <td>{statusTag(item.status)}</td>
                      <td>{new Date(item.createdAtUtc).toLocaleString()}</td>
                      <td>{new Date(item.updatedAtUtc).toLocaleString()}</td>
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
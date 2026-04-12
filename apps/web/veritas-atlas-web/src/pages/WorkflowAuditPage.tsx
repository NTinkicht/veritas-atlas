import { useEffect, useState } from "react";
import { getWorkflowAuditEntries } from "../api/workflowAudit";
import type { WorkflowAuditEntry } from "../api/contracts";
import { AppSurface } from "../components/AppSurface";
import { LoadingState } from "../components/LoadingState";
import { ErrorState } from "../components/ErrorState";
import { EmptyState } from "../components/EmptyState";
import { ProtectedNotice } from "../components/ProtectedNotice";

function statusTag(value: string | null) {
  return <span className="tag">{value ?? "N/A"}</span>;
}

function normalizeAuditItems(value: unknown): WorkflowAuditEntry[] {
  if (Array.isArray(value)) {
    return value as WorkflowAuditEntry[];
  }

  if (value && typeof value === "object") {
    const record = value as Record<string, unknown>;
    if (Array.isArray(record.items)) {
      return record.items as WorkflowAuditEntry[];
    }
  }

  return [];
}

export function WorkflowAuditPage() {
  const [items, setItems] = useState<WorkflowAuditEntry[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [authWarning, setAuthWarning] = useState<string | null>(null);

  async function load() {
    setLoading(true);
    setError(null);
    setAuthWarning(null);

    try {
      const result = await getWorkflowAuditEntries();
      setItems(normalizeAuditItems(result));
    } catch (err) {
      const message =
        typeof err === "object" && err && "message" in err
          ? String((err as { message?: unknown }).message ?? "Failed to load audit entries.")
          : "Failed to load audit entries.";

      if (message.includes("401") || message.toLowerCase().includes("unauthorized")) {
        setAuthWarning("This page requires a fresh login.");
        setItems([]);
      } else {
        setError(message);
      }
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => {
    void load();
  }, []);

  return (
    <AppSurface
      title="Workflow Audit"
      subtitle="A readable, trustworthy timeline of workflow transitions and decisions."
    >
      <div className="action-row">
        <button onClick={() => void load()} disabled={loading}>Refresh</button>
      </div>

      {error ? <ErrorState message={error} /> : null}
      {authWarning ? <ProtectedNotice message={authWarning} /> : null}
      {loading ? <LoadingState message="Loading workflow audit..." /> : null}

      {!loading ? (
        <section className="panel">
          <div className="panel-header">
            <h2 className="panel-title">Audit Timeline</h2>
            <span className="tag">{items.length} entries</span>
          </div>

          {items.length === 0 ? (
            <EmptyState message="No workflow audit entries available." />
          ) : (
            <div className="table-wrap">
              <table className="table-card">
                <thead>
                  <tr>
                    <th>Action</th>
                    <th>Entity</th>
                    <th>Previous</th>
                    <th>Next</th>
                    <th>Role</th>
                    <th>Succeeded</th>
                    <th>Time</th>
                  </tr>
                </thead>
                <tbody>
                  {items.map((entry) => (
                    <tr key={entry.id}>
                      <td>{entry.actionName}</td>
                      <td>{entry.entityType}</td>
                      <td>{statusTag(entry.previousStatus)}</td>
                      <td>{statusTag(entry.nextStatus)}</td>
                      <td>{entry.role}</td>
                      <td>{entry.succeeded ? "Yes" : "No"}</td>
                      <td>{new Date(entry.timestampUtc).toLocaleString()}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </section>
      ) : null}
    </AppSurface>
  );
}
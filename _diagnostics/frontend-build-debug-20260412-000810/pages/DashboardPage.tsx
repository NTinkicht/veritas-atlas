import { useEffect, useState } from "react";
import { Link } from "react-router-dom";
import { getCases } from "../api/cases";
import { getClaims } from "../api/claims";
import { getContradictions } from "../api/contradictions";
import { getWorkflowAuditEntries } from "../api/workflowAudit";
import { getScenarioSnapshot } from "../api/persistence";
import { hasAccessToken } from "../api/httpAuth";
import type {
  CaseItem,
  ClaimItem,
  ContradictionItem,
  PagedResponse,
  ScenarioSnapshotResponse,
  WorkflowAuditEntry,
} from "../api/contracts";
import { AppSurface } from "../components/AppSurface";
import { LoadingState } from "../components/LoadingState";
import { ErrorState } from "../components/ErrorState";
import { EmptyState } from "../components/EmptyState";
import { ProtectedNotice } from "../components/ProtectedNotice";

type DashboardState = {
  cases: PagedResponse<CaseItem> | null;
  claims: PagedResponse<ClaimItem> | null;
  contradictions: PagedResponse<ContradictionItem> | null;
  audit: WorkflowAuditEntry[];
  snapshot: ScenarioSnapshotResponse | null;
};

function statusTag(value: string | null | undefined) {
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

export function DashboardPage() {
  const [data, setData] = useState<DashboardState>({
    cases: null,
    claims: null,
    contradictions: null,
    audit: [],
    snapshot: null,
  });
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [authWarning, setAuthWarning] = useState<string | null>(null);

  async function load() {
    setLoading(true);
    setError(null);
    setAuthWarning(null);

    try {
      const [cases, claims, contradictions] = await Promise.all([
        getCases(1, 5),
        getClaims(1, 5),
        getContradictions(1, 5),
      ]);

      let audit: WorkflowAuditEntry[] = [];
      let snapshot: ScenarioSnapshotResponse | null = null;

      try {
        const [auditResult, snapshotResult] = await Promise.all([
          getWorkflowAuditEntries(),
          getScenarioSnapshot(),
        ]);

        audit = normalizeAuditItems(auditResult).slice(0, 5);
        snapshot = snapshotResult ?? null;
      } catch (err) {
        const message =
          typeof err === "object" && err && "message" in err
            ? String((err as { message?: unknown }).message ?? "")
            : "";

        if (message.includes("401") || message.toLowerCase().includes("unauthorized")) {
          setAuthWarning("Protected panels require a fresh login.");
        } else {
          setError(message || "Failed to load protected dashboard panels.");
        }
      }

      setData({
        cases,
        claims,
        contradictions,
        audit,
        snapshot,
      });
    } catch (err) {
      const message =
        typeof err === "object" && err && "message" in err
          ? String((err as { message?: unknown }).message ?? "Failed to load dashboard.")
          : "Failed to load dashboard.";
      setError(message);
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => {
    void load();
  }, []);

  return (
    <AppSurface
      title="Operational Dashboard"
      subtitle="A clean command center for cases, claims, contradictions, workflow activity, and snapshot state."
    >
      <div className="action-row">
        <button onClick={() => void load()} disabled={loading}>Refresh</button>
      </div>

      {!hasAccessToken() ? <ProtectedNotice message="You are not logged in." /> : null}
      {error ? <ErrorState message={error} /> : null}
      {authWarning ? <ProtectedNotice message={authWarning} /> : null}

      <section className="metric-grid">
        <div className="metric-card">
          <div className="metric-label">Cases</div>
          <div className="metric-value">{data.cases?.totalCount ?? 0}</div>
        </div>
        <div className="metric-card">
          <div className="metric-label">Claims</div>
          <div className="metric-value">{data.claims?.totalCount ?? 0}</div>
        </div>
        <div className="metric-card">
          <div className="metric-label">Contradictions</div>
          <div className="metric-value">{data.contradictions?.totalCount ?? 0}</div>
        </div>
        <div className="metric-card">
          <div className="metric-label">Snapshot</div>
          <div className="metric-value">{data.snapshot?.exists ? "Yes" : "No"}</div>
        </div>
      </section>

      {loading ? (
        <LoadingState message="Loading dashboard..." />
      ) : (
        <>
          <section className="grid-2">
            <div className="panel">
              <div className="panel-header">
                <h2 className="panel-title">Recent Cases</h2>
                <Link className="panel-link" to="/cases">Open</Link>
              </div>
              {(data.cases?.items?.length ?? 0) === 0 ? (
                <EmptyState message="No cases available." />
              ) : (
                <div className="table-wrap">
                  <table className="table-card">
                    <thead>
                      <tr>
                        <th>Title</th>
                        <th>Status</th>
                        <th>Created</th>
                      </tr>
                    </thead>
                    <tbody>
                      {(data.cases?.items ?? []).map((item) => (
                        <tr key={item.id}>
                          <td><Link to={`/cases/${item.id}`}>{item.title}</Link></td>
                          <td>{statusTag(item.status)}</td>
                          <td>{new Date(item.createdAtUtc).toLocaleString()}</td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              )}
            </div>

            <div className="panel">
              <div className="panel-header">
                <h2 className="panel-title">Snapshot</h2>
              </div>
              {data.snapshot?.exists ? (
                <div className="kv-grid">
                  <div className="kv-row"><div className="kv-label">Case</div><div className="kv-value">{data.snapshot.caseId}</div></div>
                  <div className="kv-row"><div className="kv-label">Claim A</div><div className="kv-value">{data.snapshot.claimAId}</div></div>
                  <div className="kv-row"><div className="kv-label">Claim B</div><div className="kv-value">{data.snapshot.claimBId}</div></div>
                  <div className="kv-row"><div className="kv-label">Contradiction</div><div className="kv-value">{data.snapshot.contradictionId}</div></div>
                  <div className="kv-row"><div className="kv-label">Case Status</div><div className="kv-value">{data.snapshot.caseStatus ?? "N/A"}</div></div>
                  <div className="kv-row"><div className="kv-label">Contradiction Status</div><div className="kv-value">{data.snapshot.contradictionStatus ?? "N/A"}</div></div>
                </div>
              ) : (
                <EmptyState message="No active snapshot." />
              )}
            </div>
          </section>

          <section className="panel">
            <div className="panel-header">
              <h2 className="panel-title">Recent Workflow Audit</h2>
              <Link className="panel-link" to="/workflow-audit">Open</Link>
            </div>
            {data.audit.length === 0 ? (
              <EmptyState message="No workflow audit entries available." />
            ) : (
              <div className="table-wrap">
                <table className="table-card">
                  <thead>
                    <tr>
                      <th>Action</th>
                      <th>Entity</th>
                      <th>Role</th>
                      <th>Status</th>
                      <th>Time</th>
                    </tr>
                  </thead>
                  <tbody>
                    {data.audit.map((entry) => (
                      <tr key={entry.id}>
                        <td>{entry.actionName}</td>
                        <td>{entry.entityType}</td>
                        <td>{entry.role}</td>
                        <td>{statusTag(entry.nextStatus)}</td>
                        <td>{new Date(entry.timestampUtc).toLocaleString()}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}
          </section>
        </>
      )}
    </AppSurface>
  );
}
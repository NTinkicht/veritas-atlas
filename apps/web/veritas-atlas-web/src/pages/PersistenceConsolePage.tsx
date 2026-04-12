import { useEffect, useState } from "react";
import { getScenarioSnapshot, resetScenarioState } from "../api/persistence";
import type { ScenarioSnapshotResponse } from "../api/contracts";
import { AppSurface } from "../components/AppSurface";
import { LoadingState } from "../components/LoadingState";
import { ErrorState } from "../components/ErrorState";
import { EmptyState } from "../components/EmptyState";
import { ProtectedNotice } from "../components/ProtectedNotice";

export function PersistenceConsolePage() {
  const [snapshot, setSnapshot] = useState<ScenarioSnapshotResponse | null>(null);
  const [loading, setLoading] = useState(true);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [authWarning, setAuthWarning] = useState<string | null>(null);
  const [message, setMessage] = useState<string | null>(null);

  async function load() {
    setLoading(true);
    setError(null);
    setAuthWarning(null);

    try {
      const result = await getScenarioSnapshot();
      setSnapshot(result);
    } catch (err) {
      const msg =
        typeof err === "object" && err && "message" in err
          ? String((err as { message?: unknown }).message ?? "Failed to load snapshot.")
          : "Failed to load snapshot.";

      if (msg.includes("401") || msg.toLowerCase().includes("unauthorized")) {
        setAuthWarning("This page requires a fresh login.");
      } else {
        setError(msg);
      }
    } finally {
      setLoading(false);
    }
  }

  async function reset() {
    setBusy(true);
    setError(null);
    setMessage(null);

    try {
      const result = await resetScenarioState();
      setMessage(result.message);
      await load();
    } catch (err) {
      const msg =
        typeof err === "object" && err && "message" in err
          ? String((err as { message?: unknown }).message ?? "Failed to reset snapshot.")
          : "Failed to reset snapshot.";
      setError(msg);
    } finally {
      setBusy(false);
    }
  }

  useEffect(() => {
    void load();
  }, []);

  return (
    <AppSurface
      title="Persistence Console"
      subtitle="Control and inspect the active seeded lifecycle snapshot."
    >
      <div className="action-row">
        <button onClick={() => void load()} disabled={loading || busy}>Refresh</button>
        <button onClick={() => void reset()} disabled={busy}>{busy ? "Resetting..." : "Reset Snapshot"}</button>
      </div>

      {error ? <ErrorState message={error} /> : null}
      {authWarning ? <ProtectedNotice message={authWarning} /> : null}
      {message ? <div className="notice-card notice-success">{message}</div> : null}

      <section className="panel">
        <div className="panel-header">
          <h2 className="panel-title">Active Snapshot</h2>
        </div>

        {loading ? (
          <LoadingState message="Loading snapshot..." />
        ) : snapshot?.exists ? (
          <div className="kv-grid">
            <div className="kv-row"><div className="kv-label">Case</div><div className="kv-value">{snapshot.caseId}</div></div>
            <div className="kv-row"><div className="kv-label">Claim A</div><div className="kv-value">{snapshot.claimAId}</div></div>
            <div className="kv-row"><div className="kv-label">Claim B</div><div className="kv-value">{snapshot.claimBId}</div></div>
            <div className="kv-row"><div className="kv-label">Contradiction</div><div className="kv-value">{snapshot.contradictionId}</div></div>
            <div className="kv-row"><div className="kv-label">Case Status</div><div className="kv-value">{snapshot.caseStatus ?? "N/A"}</div></div>
            <div className="kv-row"><div className="kv-label">Contradiction Status</div><div className="kv-value">{snapshot.contradictionStatus ?? "N/A"}</div></div>
            <div className="kv-row"><div className="kv-label">Created</div><div className="kv-value">{snapshot.createdAtUtc ? new Date(snapshot.createdAtUtc).toLocaleString() : "N/A"}</div></div>
          </div>
        ) : (
          <EmptyState message="No active snapshot." />
        )}
      </section>
    </AppSurface>
  );
}
import { useEffect, useState } from "react";
import { clearWorkflowAudit } from "../api/workflowActions";
import { resetPersistenceSnapshot } from "../api/workflowActions";
import {
  preparePublication,
  resolveContradiction,
  seedLifecycle,
  type SeedLifecycleResponse,
} from "../api/seededWorkflow";
import { ToastMessage } from "./ToastMessage";

type SeededWorkflowHarnessProps = {
  onRefresh?: () => Promise<void> | void;
};

type ToastState = {
  type: "success" | "error";
  message: string;
} | null;

export function SeededWorkflowHarness({ onRefresh }: SeededWorkflowHarnessProps) {
  const [seed, setSeed] = useState<SeedLifecycleResponse | null>(null);
  const [busy, setBusy] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [message, setMessage] = useState<string | null>(null);
  const [toast, setToast] = useState<ToastState>(null);

  useEffect(() => {
    if (!toast) {
      return;
    }

    const id = window.setTimeout(() => setToast(null), 3000);
    return () => window.clearTimeout(id);
  }, [toast]);

  async function refreshAfterAction() {
    if (onRefresh) {
      await onRefresh();
    }
  }

  async function run(label: string, fn: () => Promise<unknown>) {
    setBusy(label);
    setError(null);
    setMessage(null);

    try {
      const result = await fn();

      if (label === "Seed Lifecycle" && result && typeof result === "object") {
        setSeed(result as SeedLifecycleResponse);
      }

      const resultMessage =
        result && typeof result === "object" && "message" in result
          ? String((result as { message?: unknown }).message ?? `${label} completed successfully.`)
          : `${label} completed successfully.`;

      setMessage(resultMessage);
      setToast({ type: "success", message: resultMessage });
      await refreshAfterAction();
    } catch (err) {
      const text =
        typeof err === "object" && err && "message" in err
          ? String((err as { message?: unknown }).message ?? `${label} failed.`)
          : `${label} failed.`;
      setError(text);
      setToast({ type: "error", message: text });
    } finally {
      setBusy(null);
    }
  }

  return (
    <>
      {toast ? <ToastMessage type={toast.type} message={toast.message} /> : null}

      <section className="panel">
        <div className="panel-header">
          <h2 className="panel-title">Seeded Workflow Test Harness</h2>
        </div>

        <div className="notice-card" style={{ marginBottom: 12 }}>
          Use this harness to test the known-good workflow path: Seed Lifecycle â†’ Prepare Publication â†’ Resolve Contradiction â†’ Reset Snapshot.
        </div>

        {message ? <div className="notice-card notice-success">{message}</div> : null}
        {error ? <div className="notice-card notice-danger">{error}</div> : null}

        <div className="action-row">
          <button disabled={!!busy} onClick={() => void run("Clear Workflow Audit", clearWorkflowAudit)}>
            {busy === "Clear Workflow Audit" ? "Clearing..." : "Clear Workflow Audit"}
          </button>

          <button disabled={!!busy} onClick={() => void run("Reset Persistence Snapshot", resetPersistenceSnapshot)}>
            {busy === "Reset Persistence Snapshot" ? "Resetting..." : "Reset Persistence Snapshot"}
          </button>

          <button disabled={!!busy} onClick={() => void run("Seed Lifecycle", seedLifecycle)}>
            {busy === "Seed Lifecycle" ? "Seeding..." : "Seed Lifecycle"}
          </button>
        </div>

        <div className="action-row" style={{ marginTop: 12 }}>
          <button
            disabled={!seed?.caseId || !!busy}
            onClick={() => void run("Prepare Publication", () => preparePublication(seed!.caseId))}
          >
            {busy === "Prepare Publication" ? "Preparing..." : "Prepare Publication"}
          </button>

          <button
            disabled={!seed?.contradictionId || !!busy}
            onClick={() => void run("Resolve Contradiction", () => resolveContradiction(seed!.contradictionId))}
          >
            {busy === "Resolve Contradiction" ? "Resolving..." : "Resolve Contradiction"}
          </button>
        </div>

        {seed ? (
          <div className="kv-grid" style={{ marginTop: 16 }}>
            <div className="kv-row"><div className="kv-label">Case Id</div><div className="kv-value">{seed.caseId}</div></div>
            <div className="kv-row"><div className="kv-label">Claim A Id</div><div className="kv-value">{seed.claimAId}</div></div>
            <div className="kv-row"><div className="kv-label">Claim B Id</div><div className="kv-value">{seed.claimBId}</div></div>
            <div className="kv-row"><div className="kv-label">Contradiction Id</div><div className="kv-value">{seed.contradictionId}</div></div>
            <div className="kv-row"><div className="kv-label">Case Status</div><div className="kv-value">{seed.caseStatus}</div></div>
            <div className="kv-row"><div className="kv-label">Contradiction Status</div><div className="kv-value">{seed.contradictionStatus}</div></div>
          </div>
        ) : (
          <div className="empty-card" style={{ marginTop: 16 }}>
            <strong style={{ display: "block", marginBottom: 8 }}>No active seeded scenario</strong>
            <span>Run Seed Lifecycle first to generate a fresh test workflow.</span>
          </div>
        )}
      </section>
    </>
  );
}
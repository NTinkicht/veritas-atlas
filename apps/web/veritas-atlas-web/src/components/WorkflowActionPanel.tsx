import { useEffect, useMemo, useState } from "react";
import type { CaseWorkflowAction } from "../api/workflowActions";
import {
  clearWorkflowAudit,
  getAllowedCaseActions,
  resetPersistenceSnapshot,
  runCaseWorkflowAction,
} from "../api/workflowActions";
import { ToastMessage } from "./ToastMessage";

type WorkflowActionPanelProps = {
  caseId?: string | null;
  caseStatus?: string | null;
  onRefresh?: () => Promise<void> | void;
};

type ToastState = {
  type: "success" | "error";
  message: string;
} | null;

const labels: Record<CaseWorkflowAction, string> = {
  submit: "Submit Case",
  approve: "Approve",
  reject: "Reject",
  preparePublication: "Prepare Publication",
  publish: "Publish",
  hold: "Hold",
};

function isDestructive(action: CaseWorkflowAction | "clearAudit" | "resetPersistence"): boolean {
  return action === "reject" || action === "publish" || action === "hold" || action === "clearAudit" || action === "resetPersistence";
}

export function WorkflowActionPanel({ caseId, caseStatus, onRefresh }: WorkflowActionPanelProps) {
  const [busy, setBusy] = useState<string | null>(null);
  const [inlineMessage, setInlineMessage] = useState<string | null>(null);
  const [inlineError, setInlineError] = useState<string | null>(null);
  const [toast, setToast] = useState<ToastState>(null);

  const allowedActions = useMemo(() => getAllowedCaseActions(caseStatus), [caseStatus]);

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

  async function runCaseAction(action: CaseWorkflowAction) {
    if (!caseId) {
      setInlineError("A case id is required.");
      setToast({ type: "error", message: "A case id is required." });
      return;
    }

    const label = labels[action];

    if (isDestructive(action)) {
      const confirmed = window.confirm(`Are you sure you want to ${label.toLowerCase()}?`);
      if (!confirmed) {
        return;
      }
    }

    setBusy(label);
    setInlineError(null);
    setInlineMessage(null);

    try {
      const result = await runCaseWorkflowAction(caseId, action);
      const message = result.message || `${label} completed successfully.`;
      setInlineMessage(message);
      setToast({ type: "success", message });
      await refreshAfterAction();
    } catch (err) {
      const message =
        typeof err === "object" && err && "message" in err
          ? String((err as { message?: unknown }).message ?? `${label} failed.`)
          : `${label} failed.`;
      setInlineError(message);
      setToast({ type: "error", message });
    } finally {
      setBusy(null);
    }
  }

  async function runGlobalAction(kind: "clearAudit" | "resetPersistence") {
    const label = kind === "clearAudit" ? "Clear Workflow Audit" : "Reset Persistence Snapshot";

    if (isDestructive(kind)) {
      const confirmed = window.confirm(`Are you sure you want to ${label.toLowerCase()}?`);
      if (!confirmed) {
        return;
      }
    }

    setBusy(label);
    setInlineError(null);
    setInlineMessage(null);

    try {
      const result =
        kind === "clearAudit"
          ? await clearWorkflowAudit()
          : await resetPersistenceSnapshot();

      const message = result.message || `${label} completed successfully.`;
      setInlineMessage(message);
      setToast({ type: "success", message });
      await refreshAfterAction();
    } catch (err) {
      const message =
        typeof err === "object" && err && "message" in err
          ? String((err as { message?: unknown }).message ?? `${label} failed.`)
          : `${label} failed.`;
      setInlineError(message);
      setToast({ type: "error", message });
    } finally {
      setBusy(null);
    }
  }

  return (
    <>
      {toast ? <ToastMessage type={toast.type} message={toast.message} /> : null}

      <section className="panel">
        <div className="panel-header">
          <h2 className="panel-title">Workflow Actions</h2>
          <span className="tag">{caseStatus ?? "Unknown status"}</span>
        </div>

        {inlineMessage ? <div className="notice-card notice-success">{inlineMessage}</div> : null}
        {inlineError ? <div className="notice-card notice-danger">{inlineError}</div> : null}

        {allowedActions.length === 0 ? (
          <div className="empty-card">
            <strong style={{ display: "block", marginBottom: 8 }}>No case actions available</strong>
            <span>The current case status does not allow any further supported case workflow actions.</span>
          </div>
        ) : (
          <div className="action-row">
            {allowedActions.map((action) => {
              const label = labels[action];
              return (
                <button
                  key={action}
                  disabled={!caseId || !!busy}
                  onClick={() => void runCaseAction(action)}
                >
                  {busy === label ? `${label}...` : label}
                </button>
              );
            })}
          </div>
        )}

        <div className="action-row" style={{ marginTop: 12 }}>
          <button disabled={!!busy} onClick={() => void runGlobalAction("clearAudit")}>
            {busy === "Clear Workflow Audit" ? "Clearing..." : "Clear Workflow Audit"}
          </button>

          <button disabled={!!busy} onClick={() => void runGlobalAction("resetPersistence")}>
            {busy === "Reset Persistence Snapshot" ? "Resetting..." : "Reset Persistence Snapshot"}
          </button>
        </div>
      </section>
    </>
  );
}
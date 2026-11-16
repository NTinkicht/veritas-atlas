import type { LifecycleAction, PublicationRevision, RevisionFailure } from "./publicationRevisions";

export type TransitionFlow = {
  id: string;
  action: LifecycleAction;
  version: number;
  operationId: string;
  /** True only while the revision this transition started on is still the one on screen. */
  isCurrent: () => boolean;
  transition: (id: string, action: LifecycleAction, version: number, operationId: string) => Promise<{ replayed: boolean; revision: PublicationRevision }>;
  classify: (error: unknown) => RevisionFailure;
  onAccepted: (notice: string) => void;
  onRefused: (failure: RevisionFailure) => void;
  reload: (id: string) => Promise<void>;
};

/**
 * Runs one lifecycle transition and applies its outcome to the screen only if the
 * operator is still looking at the revision it was started on. The server request is
 * never cancelled; only the stale UI side effects (notice, error, reload) are dropped,
 * so a late completion cannot overwrite a different revision's panel.
 */
export async function runTransitionFlow(flow: TransitionFlow): Promise<void> {
  try {
    const result = await flow.transition(flow.id, flow.action, flow.version, flow.operationId);
    if (!flow.isCurrent()) return;
    flow.onAccepted(`${flow.action} accepted by the server${result.replayed ? " (already applied)" : ""}. Now ${result.revision.state}.`);
    await flow.reload(flow.id);
  } catch (error) {
    if (!flow.isCurrent()) return;
    flow.onRefused(flow.classify(error));
  }
}

/**
 * Reconciles the review queue with a revision the server just returned: the matching row is
 * replaced in place (so it never shows a stale state/version); when a state filter is active
 * and the revision no longer matches it, the row is dropped. Unknown ids leave the list as is.
 */
export function reconcileQueueItem(
  items: PublicationRevision[],
  revision: PublicationRevision,
  filter: PublicationRevision["state"] | null,
): PublicationRevision[] {
  if (!items.some((i) => i.id === revision.id)) return items;
  if (filter !== null && revision.state !== filter) return items.filter((i) => i.id !== revision.id);
  return items.map((i) => (i.id === revision.id ? revision : i));
}

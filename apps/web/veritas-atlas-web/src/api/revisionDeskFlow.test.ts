import { describe, expect, it, vi } from "vitest";
import type { PublicationRevision } from "./publicationRevisions";
import { reconcileQueueItem, runCreateDraftFlow, runTransitionFlow, type TransitionFlow } from "./revisionDeskFlow";

// SYNTHETIC fixtures only.
const revision: PublicationRevision = {
  id: "11111111-1111-4111-8111-111111111111", authorId: "analyst", contentDigest: "a".repeat(64),
  approvedDigest: null, policyPermitsPublicDisplay: false, state: "Approved", version: 3, factsVersion: 0,
};
const base = (over: Partial<TransitionFlow> = {}) => {
  const onAccepted = vi.fn(), onRefused = vi.fn(), reload = vi.fn(async () => undefined);
  const flow: TransitionFlow = {
    id: revision.id, action: "Approve", version: 2, operationId: "op-1", isCurrent: () => true,
    transition: async () => ({ replayed: false, revision }), classify: () => ({ kind: "server", message: "boom" }),
    onAccepted, onRefused, reload, ...over,
  };
  return { flow, onAccepted, onRefused, reload };
};

describe("runTransitionFlow", () => {
  it("applies notice and reloads when the revision is still on screen", async () => {
    const { flow, onAccepted, reload } = base();
    await runTransitionFlow(flow);
    expect(onAccepted).toHaveBeenCalledWith("Approve accepted by the server. Now Approved.");
    expect(reload).toHaveBeenCalledWith(revision.id);
  });
  it("reports a refusal when the revision is still on screen", async () => {
    const { flow, onRefused, reload } = base({ transition: async () => { throw new Error("x"); } });
    await runTransitionFlow(flow);
    expect(onRefused).toHaveBeenCalledWith({ kind: "server", message: "boom" });
    expect(reload).not.toHaveBeenCalled();
  });
  it("drops a late success when another revision was opened meanwhile", async () => {
    let current = true;
    const { flow, onAccepted, onRefused, reload } = base({ isCurrent: () => current, transition: async () => { current = false; return { replayed: false, revision }; } });
    await runTransitionFlow(flow);
    expect(onAccepted).not.toHaveBeenCalled();
    expect(reload).not.toHaveBeenCalled();
    expect(onRefused).not.toHaveBeenCalled();
  });
  it("drops a late refusal when another revision was opened meanwhile", async () => {
    let current = true;
    const { flow, onRefused, reload } = base({ isCurrent: () => current, transition: async () => { current = false; throw new Error("x"); } });
    await runTransitionFlow(flow);
    expect(onRefused).not.toHaveBeenCalled();
    expect(reload).not.toHaveBeenCalled();
  });
});

describe("reconcileQueueItem", () => {
  const stale = { ...revision, state: "Submitted" as const, version: 2 };
  const other = { ...revision, id: "22222222-2222-4222-8222-222222222222" };
  it("replaces the stale row in place with the server's new state and version", () => {
    const out = reconcileQueueItem([other, stale], revision, null);
    expect(out).toEqual([other, revision]);
  });
  it("drops the row when it no longer matches the active state filter, keeps it when it does", () => {
    expect(reconcileQueueItem([other, stale], revision, "Submitted")).toEqual([other]);
    expect(reconcileQueueItem([other, stale], revision, "Approved")).toEqual([other, revision]);
  });
  it("leaves the queue untouched for a revision that is not listed", () => {
    const list = [other];
    expect(reconcileQueueItem(list, revision, null)).toBe(list);
  });
});

describe("runCreateDraftFlow", () => {
  const made = { id: "11111111-1111-4111-8111-111111111111", authorId: "a", contentDigest: "a".repeat(64), approvedDigest: null, policyPermitsPublicDisplay: false, state: "Draft" as const, version: 0, factsVersion: 0 };
  const classify = (e: unknown) => ({ kind: "forbidden" as const, message: String((e as Error).message) });
  it("reports the created revision only after the server returned it", async () => {
    const onCreated = vi.fn(); const onRefused = vi.fn();
    await runCreateDraftFlow({ digest: "a".repeat(64), policyPermitsPublicDisplay: false, create: async () => made, classify, onCreated, onRefused });
    expect(onCreated).toHaveBeenCalledWith(made);
    expect(onRefused).not.toHaveBeenCalled();
  });
  it("surfaces a refusal and never reports success", async () => {
    const onCreated = vi.fn(); const onRefused = vi.fn();
    await runCreateDraftFlow({ digest: "a".repeat(64), policyPermitsPublicDisplay: true, create: async () => { throw new Error("denied"); }, classify, onCreated, onRefused });
    expect(onCreated).not.toHaveBeenCalled();
    expect(onRefused).toHaveBeenCalledWith({ kind: "forbidden", message: "denied" });
  });
});

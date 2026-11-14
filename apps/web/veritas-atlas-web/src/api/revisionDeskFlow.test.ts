import { describe, expect, it, vi } from "vitest";
import type { PublicationRevision } from "./publicationRevisions";
import { runTransitionFlow, type TransitionFlow } from "./revisionDeskFlow";

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

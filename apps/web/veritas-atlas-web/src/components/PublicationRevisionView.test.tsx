import { renderToStaticMarkup } from "react-dom/server";
import { describe, expect, it } from "vitest";
import type { PublicationRevision } from "../api/publicationRevisions";
import { RevisionPanel, type Loadable } from "./PublicationRevisionView";

// SYNTHETIC fixtures only.
const revision: PublicationRevision = {
  id: "11111111-1111-4111-8111-111111111111", authorId: "<b>analyst</b>", contentDigest: "a".repeat(64),
  approvedDigest: null, policyPermitsPublicDisplay: false, state: "Submitted", version: 2, factsVersion: 0,
};
const render = (view: Loadable<PublicationRevision>, extra: Partial<Parameters<typeof RevisionPanel>[0]> = {}) =>
  renderToStaticMarkup(
    <RevisionPanel view={view} busy={false} notice={null} actionFailure={null} onAction={() => undefined} signInHref="/login?returnTo=%2Fpublication-revisions" {...extra} />,
  );

describe("RevisionPanel", () => {
  it("shows an honest idle prompt and no actions before a revision is opened", () => {
    const html = render({ kind: "idle" });
    expect(html).toContain("Enter a publication revision id");
    expect(html).not.toContain("Approve");
  });
  it("shows state, version, escaped author and all six actions", () => {
    const html = render({ kind: "loaded", data: revision });
    expect(html).toContain("Submitted");
    expect(html).toContain("&lt;b&gt;analyst&lt;/b&gt;");
    expect(html).not.toContain("<b>analyst");
    for (const a of ["Submit", "RequestChanges", "Approve", "Revise", "Publish", "Withdraw"]) expect(html).toContain(`>${a}</button>`);
  });
  it("disables actions while busy", () => {
    expect(render({ kind: "loaded", data: revision }, { busy: true })).toContain("disabled");
    expect(render({ kind: "loaded", data: revision })).not.toContain("disabled");
  });
  it("shows a refusal as an alert and no success notice", () => {
    const html = render({ kind: "loaded", data: revision }, { actionFailure: { kind: "forbidden", message: "Not permitted." } });
    expect(html).toContain('role="alert"');
    expect(html).toContain("Not permitted.");
    expect(html).not.toContain("accepted by the server");
  });
  it("offers sign-in on 401 and renders no revision", () => {
    const html = render({ kind: "error", failure: { kind: "unauthenticated", message: "Ended." } });
    expect(html).toContain('href="/login?returnTo=%2Fpublication-revisions"');
    expect(html).not.toContain("Lifecycle actions");
  });
});

import { renderToStaticMarkup } from "react-dom/server";
import { describe, expect, it } from "vitest";
import type { PublicationRevision } from "../api/publicationRevisions";
import { RevisionQueue, type QueueView } from "./PublicationRevisionQueue";

// SYNTHETIC fixtures only.
const item: PublicationRevision = {
  id: "11111111-1111-4111-8111-111111111111", authorId: "<b>analyst</b>", contentDigest: "a".repeat(64),
  approvedDigest: null, policyPermitsPublicDisplay: false, state: "Submitted", version: 2, factsVersion: 0,
};
const base: QueueView = { items: [], nextAfterId: null, loading: false, failure: null, loaded: true };
const render = (queue: Partial<QueueView>, state: Parameters<typeof RevisionQueue>[0]["state"] = null) =>
  renderToStaticMarkup(
    <RevisionQueue queue={{ ...base, ...queue }} state={state} onStateChange={() => undefined} onOpen={() => undefined} onLoadMore={() => undefined} signInHref="/login" />,
  );

describe("RevisionQueue", () => {
  it("lists items with escaped author text and an open button per item", () => {
    const html = render({ items: [item] });
    expect(html).toContain("&lt;b&gt;analyst&lt;/b&gt;");
    expect(html).not.toContain("<b>analyst");
    expect(html).toContain(`Open ${item.id}`);
    expect(html).not.toContain("No publication revisions match.");
  });
  it("shows an honest empty state only after a successful load", () => {
    expect(render({})).toContain("No publication revisions match.");
    expect(render({ loaded: false })).not.toContain("No publication revisions match.");
    expect(render({ loaded: true, failure: { kind: "server", message: "Failed." } })).not.toContain("No publication revisions match.");
  });
  it("offers Load more only when the server returned a cursor", () => {
    expect(render({ items: [item], nextAfterId: "22222222-2222-4222-8222-222222222222" })).toContain("Load more");
    expect(render({ items: [item] })).not.toContain("Load more");
  });
  it("offers every lifecycle state as a filter and marks the selected one", () => {
    const html = render({}, "Approved");
    for (const s of ["Draft", "Submitted", "ChangesRequested", "Approved", "Published", "Withdrawn"]) expect(html).toContain(`>${s}</option>`);
    expect(html).toMatch(/<option value="Approved" selected/);
  });
  it("shows a refusal as an alert with sign-in on 401, keeping no fabricated items", () => {
    const html = render({ failure: { kind: "unauthenticated", message: "Ended." } });
    expect(html).toContain('role="alert"');
    expect(html).toContain('href="/login"');
    expect(html).not.toContain("Open ");
  });
});

import { renderToStaticMarkup } from "react-dom/server";
import { describe, expect, it } from "vitest";
import { PublicationDraftForm } from "./PublicationDraftForm";

describe("PublicationDraftForm", () => {
  it("renders the digest field and a public-display checkbox that is off by default", () => {
    const html = renderToStaticMarkup(<PublicationDraftForm busy={false} onCreate={() => undefined} />);
    expect(html).toContain("Create a draft");
    expect(html).toContain("Policy permits public display");
    expect(html).not.toContain("checked");
    expect(html).not.toContain("Nothing was sent");
  });
  it("disables submit while a request is in flight", () => {
    const html = renderToStaticMarkup(<PublicationDraftForm busy onCreate={() => undefined} />);
    expect(html).toMatch(/<button[^>]*disabled[^>]*>Create draft/);
  });
});

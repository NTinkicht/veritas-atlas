// @vitest-environment jsdom
import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { PublicationCommentsPanel } from "./PublicationCommentsPanel";

// SYNTHETIC fixtures only. The network clients are mocked; the panel wiring is real.
const api = vi.hoisted(() => ({ listComments: vi.fn(), addComment: vi.fn() }));
vi.mock("../api/publicationComments", async (o) => ({
  ...(await o<typeof import("../api/publicationComments")>()),
  listComments: api.listComments,
  addComment: api.addComment,
}));

const REV = "22222222-2222-4222-8222-222222222222";
const c1 = { id: "11111111-1111-4111-8111-111111111111", author: "reviewer.one", body: "<b>cite</b> the amendment", createdAtUtc: "2026-11-16T09:00:00Z" };
const c2 = { id: "44444444-4444-4444-8444-444444444444", author: "publisher.two", body: "Done.", createdAtUtc: "2026-11-16T10:00:00Z" };
const type = (text: string) => fireEvent.change(screen.getByLabelText("New comment"), { target: { value: text } });
const send = () => fireEvent.click(screen.getByRole("button", { name: "Add comment" }));
const setup = (id = REV) => render(<PublicationCommentsPanel key={id} revisionId={id} signInHref="/login" />);

beforeEach(() => {
  api.listComments.mockReset().mockResolvedValue([c1]);
  api.addComment.mockReset();
});
afterEach(cleanup);

describe("PublicationCommentsPanel", () => {
  it("lists comments as text, never as HTML", async () => {
    const { container } = setup();
    await screen.findByText("<b>cite</b> the amendment");
    expect(container.querySelector("b")).toBeNull();
    expect(screen.getByText("reviewer.one")).toBeTruthy();
  });

  it("shows an honest empty state and a retryable load failure", async () => {
    api.listComments.mockRejectedValueOnce({ status: 500 }).mockResolvedValueOnce([]);
    setup();
    expect((await screen.findByRole("alert")).textContent).toContain("HTTP 500");
    fireEvent.click(screen.getByRole("button", { name: "Reload comments" }));
    await screen.findByText("This revision has no comments.");
  });

  it("adds a comment only after the server accepts it, with a trimmed body and a client key", async () => {
    api.addComment.mockResolvedValue(c2);
    setup();
    await screen.findByText("reviewer.one");
    type("  Done.  ");
    send();
    await screen.findByText("publisher.two");
    expect(api.addComment).toHaveBeenCalledTimes(1);
    const [rev, key, body] = api.addComment.mock.calls[0];
    expect(rev).toBe(REV);
    expect(key).toMatch(/^[0-9a-f-]{36}$/);
    expect(body).toBe("Done.");
    expect((screen.getByLabelText("New comment") as HTMLTextAreaElement).value).toBe("");
  });

  it("keeps text typed while the save is pending, but clears the submitted text (positive control)", async () => {
    let resolve!: (v: typeof c2) => void;
    api.addComment.mockReturnValueOnce(new Promise((r) => { resolve = r; }));
    setup();
    await screen.findByText("reviewer.one");
    type("First");
    send();
    type("First and more");
    resolve(c2);
    await screen.findByText("publisher.two");
    expect((screen.getByLabelText("New comment") as HTMLTextAreaElement).value).toBe("First and more");
  });

  it("keeps the draft and shows nothing new when the server refuses (positive control: accepted above)", async () => {
    api.addComment.mockRejectedValue({ status: 403 });
    setup();
    await screen.findByText("reviewer.one");
    type("Done.");
    send();
    expect((await screen.findByRole("alert")).textContent).toContain("may not use publication comments");
    expect(screen.queryByText("publisher.two")).toBeNull();
    expect((screen.getByLabelText("New comment") as HTMLTextAreaElement).value).toBe("Done.");
  });

  it("reuses the clientKey when the same unsent text is retried, and changes it for new text", async () => {
    api.addComment.mockRejectedValueOnce({ status: 503 }).mockRejectedValueOnce({ status: 503 }).mockResolvedValue(c2);
    setup();
    await screen.findByText("reviewer.one");
    type("Done.");
    send();
    await screen.findByRole("alert");
    send();
    await waitFor(() => expect(api.addComment).toHaveBeenCalledTimes(2));
    await waitFor(() => expect((screen.getByRole("button", { name: "Add comment" }) as HTMLButtonElement).disabled).toBe(false));
    type("Done differently.");
    send();
    await waitFor(() => expect(api.addComment).toHaveBeenCalledTimes(3));
    const keys = api.addComment.mock.calls.map((c) => c[1]);
    expect(keys[0]).toBe(keys[1]);
    expect(keys[2]).not.toBe(keys[0]);
  });

  it("sends nothing for an empty, whitespace-only, over-long or control-character body", async () => {
    setup();
    await screen.findByText("reviewer.one");
    for (const bad of ["", "   ", "a".repeat(2001), "bad\u0000body"]) {
      type(bad);
      expect((screen.getByRole("button", { name: "Add comment" }) as HTMLButtonElement).disabled).toBe(true);
      send();
    }
    expect(api.addComment).not.toHaveBeenCalled();
    type("fine");
    expect((screen.getByRole("button", { name: "Add comment" }) as HTMLButtonElement).disabled).toBe(false);
  });

  it("drops a late list response after the panel moved to another revision", async () => {
    let finish!: (v: unknown) => void;
    api.listComments.mockImplementationOnce(() => new Promise((res) => { finish = res; })).mockResolvedValueOnce([c2]);
    const { rerender } = setup();
    rerender(<PublicationCommentsPanel key="other" revisionId="55555555-5555-4555-8555-555555555555" signInHref="/login" />);
    await screen.findByText("publisher.two");
    finish([c1]);
    await new Promise((r) => setTimeout(r, 20));
    expect(screen.queryByText("reviewer.one")).toBeNull();
  });
});

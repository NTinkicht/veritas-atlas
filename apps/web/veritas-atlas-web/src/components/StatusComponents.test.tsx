import { renderToStaticMarkup } from "react-dom/server";
import { describe, expect, it } from "vitest";
import { ErrorNotice } from "./ErrorNotice";
import { ErrorState } from "./ErrorState";
import { LoadingBlock } from "./LoadingBlock";
import { LoadingState } from "./LoadingState";
import { ToastMessage } from "./ToastMessage";

describe("status components expose live-region semantics", () => {
  it("announces loading politely", () => {
    for (const html of [renderToStaticMarkup(<LoadingState />), renderToStaticMarkup(<LoadingBlock />)]) {
      expect(html).toContain('role="status"');
      expect(html).toContain('aria-live="polite"');
      expect(html).toContain("Loading...");
    }
  });

  it("announces errors as alerts", () => {
    expect(renderToStaticMarkup(<ErrorState message="boom" />)).toContain('role="alert"');
    expect(renderToStaticMarkup(<ErrorNotice message="boom" />)).toContain('role="alert"');
  });

  it("renders nothing for an empty ErrorNotice", () => {
    expect(renderToStaticMarkup(<ErrorNotice message={null} />)).toBe("");
  });

  it("announces toasts by type", () => {
    expect(renderToStaticMarkup(<ToastMessage type="error" message="x" />)).toContain('role="alert"');
    const ok = renderToStaticMarkup(<ToastMessage type="success" message="x" />);
    expect(ok).toContain('role="status"');
    expect(ok).not.toContain('role="alert"');
  });
});

import { isRouteErrorResponse, useRouteError } from "react-router-dom";

export const NOT_FOUND_MESSAGE = "This page does not exist.";
export const CRASH_MESSAGE = "Something went wrong while showing this page. Reload the page or return to the start page.";

/**
 * VER-P0-19: top-level route error boundary. A render-time exception or an unmatched URL shows
 * only this fixed, generic notice. The thrown error's message, stack and any response body are
 * never rendered (they can carry internals, ids or tokens) and nothing is logged with them.
 */
export function RouteErrorBoundary() {
  const error = useRouteError();
  const notFound = isRouteErrorResponse(error) && error.status === 404;
  return (
    <main className="page-shell">
      <h1>{notFound ? "Page not found" : "Something went wrong"}</h1>
      <div className="notice-card notice-danger" role="alert">
        {notFound ? NOT_FOUND_MESSAGE : CRASH_MESSAGE}
      </div>
      <p>
        <a href="/">Back to the start page</a>
      </p>
    </main>
  );
}

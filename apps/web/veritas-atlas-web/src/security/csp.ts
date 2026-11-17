// VER-P0-17: Content-Security-Policy for the production SPA shell that serves the anonymous
// public-reader route. Built at build time only (the dev server needs inline HMR scripts).
// Fail closed: nothing is widened to a wildcard, and an unusable API base is simply omitted.

export function apiOrigin(base: string | undefined): string | null {
  if (!base) return null;
  try {
    const url = new URL(base);
    if (url.protocol !== "https:" && url.protocol !== "http:") return null;
    if (url.username || url.password) return null;
    return url.origin;
  } catch {
    return null;
  }
}

export function buildCsp(apiBase: string | undefined): string {
  const origin = apiOrigin(apiBase);
  const connect = origin ? `'self' ${origin}` : "'self'";
  return [
    "default-src 'self'",
    "script-src 'self'",
    "style-src 'self' 'unsafe-inline'",
    "img-src 'self' data:",
    `connect-src ${connect}`,
    "object-src 'none'",
    "base-uri 'none'",
    "form-action 'self'",
  ].join("; ");
}

export function cspPlugin(apiBase: string | undefined) {
  const policy = buildCsp(apiBase);
  return {
    name: "veritas-csp-meta",
    apply: "build" as const,
    transformIndexHtml() {
      return [
        {
          tag: "meta",
          attrs: { "http-equiv": "Content-Security-Policy", content: policy },
          injectTo: "head-prepend" as const,
        },
      ];
    },
  };
}

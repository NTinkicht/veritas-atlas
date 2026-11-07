// VER-P0-17 (#204): credential-free client for the anonymous public-reader projection
// (GET /api/v1/public/publications/{id}, VER-P0-16 slice 6). It never reads the stored access token,
// never sends an Authorization header or cookies, and fails closed on any unexpected payload.

const API_BASE_URL = (import.meta.env.VITE_API_BASE_URL ?? "").replace(/\/+$/, "");
const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

export const PUBLIC_PUBLICATION_PATH = "/api/v1/public/publications";

export type PublicCitation = {
  citationLabel: string;
  title: string;
  quote: string;
  sourceName: string;
  asOf: string | null;
  effectiveDate: string | null;
  freshnessLabel: string;
};

export type PublicPublication = { id: string; freshnessLabel: string; citations: PublicCitation[] };

// "hidden" is the single outcome for every non-visible cause (404 / malformed id); "error" is any other failure.
export type PublicPublicationResult =
  | { kind: "ok"; publication: PublicPublication }
  | { kind: "hidden" }
  | { kind: "error" };

const isString = (v: unknown): v is string => typeof v === "string";
const isDateOrNull = (v: unknown): v is string | null =>
  v === null || (isString(v) && !Number.isNaN(Date.parse(v)));

function parseCitation(raw: unknown): PublicCitation | null {
  if (typeof raw !== "object" || raw === null) return null;
  const c = raw as Record<string, unknown>;
  if (!isString(c.citationLabel) || !isString(c.title) || !isString(c.quote) || !isString(c.sourceName)) return null;
  if (!isString(c.freshnessLabel) || !isDateOrNull(c.asOf ?? null) || !isDateOrNull(c.effectiveDate ?? null)) return null;
  return {
    citationLabel: c.citationLabel,
    title: c.title,
    quote: c.quote,
    sourceName: c.sourceName,
    asOf: (c.asOf as string | null | undefined) ?? null,
    effectiveDate: (c.effectiveDate as string | null | undefined) ?? null,
    freshnessLabel: c.freshnessLabel,
  };
}

export function parsePublicPublication(raw: unknown): PublicPublication | null {
  if (typeof raw !== "object" || raw === null) return null;
  const p = raw as Record<string, unknown>;
  if (!isString(p.id) || !GUID.test(p.id) || !isString(p.freshnessLabel) || !Array.isArray(p.citations)) return null;
  const citations: PublicCitation[] = [];
  for (const item of p.citations) {
    const c = parseCitation(item);
    if (!c) return null;
    citations.push(c);
  }
  return { id: p.id, freshnessLabel: p.freshnessLabel, citations };
}

export async function getPublicPublication(id: string): Promise<PublicPublicationResult> {
  if (!GUID.test(id)) return { kind: "hidden" };
  try {
    const response = await fetch(`${API_BASE_URL}${PUBLIC_PUBLICATION_PATH}/${id.toLowerCase()}`, {
      method: "GET",
      headers: { Accept: "application/json" },
      credentials: "omit",
      cache: "no-store",
    });
    if (response.status === 404) return { kind: "hidden" };
    if (!response.ok) return { kind: "error" };
    const publication = parsePublicPublication(await response.json());
    return publication ? { kind: "ok", publication } : { kind: "error" };
  } catch {
    return { kind: "error" };
  }
}

import { apiPutBody } from "./http";
import { MalformedRevisionResponse, parseTransitionResponse, revisionPath, type PublicationRevision } from "./publicationRevisions";

// VER-P0-17: client for the publisher-only public-reader visibility write
// (PUT /api/v1/tenant-publication-revisions/{id}/visibility, VER-P0-16). The server alone decides
// authorization, legality and version races. This builder only refuses input that can never be
// valid (limits mirror PublicVisibilityRules) and defaults to restricted (fail closed).

export const EVIDENCE_CURRENCIES = ["Current", "NonCurrent", "Unknown"] as const;
export type EvidenceCurrency = (typeof EVIDENCE_CURRENCIES)[number];

export const VISIBILITY_LIMITS = { citations: 50, label: 100, title: 300, quote: 2000, source: 200, disclosure: 500 } as const;

export type VisibilityCitationInput = {
  citationLabel: string;
  title: string;
  quote: string;
  sourceName: string;
  freshnessLabel: string;
  asOf?: string | null;
  effectiveDate?: string | null;
  contradictionDisclosure?: string | null;
  uncertaintyLimit?: string | null;
};

export type VisibilityInput = {
  restricted: boolean;
  superseded: boolean;
  freshnessRequired: boolean;
  evidenceCurrency: EvidenceCurrency;
  historicalLabelPresent: boolean;
  expiresAt?: string | null;
  citations: VisibilityCitationInput[];
};

export function visibilityPath(id: string): string {
  return `${revisionPath(id)}/visibility`;
}

function bounded(value: string, max: number, name: string, multiline: boolean): string {
  const text = value.trim();
  if (text.length === 0 || text.length > max) throw new MalformedRevisionResponse(`${name} must be 1-${max} characters`);
  // eslint-disable-next-line no-control-regex
  const forbidden = multiline ? /[\u0000-\u0008\u000b\u000c\u000e-\u001f\u007f]/ : /[\u0000-\u001f\u007f]/;
  if (forbidden.test(text)) throw new MalformedRevisionResponse(`${name} has control characters`);
  return text;
}

function optionalDate(value: string | null | undefined, name: string): string | null {
  if (value === null || value === undefined || value.trim() === "") return null;
  if (Number.isNaN(Date.parse(value))) throw new MalformedRevisionResponse(`${name} is not a date`);
  return new Date(value).toISOString();
}

export function buildVisibilityBody(input: VisibilityInput, expectedFactsVersion: number, operationId: string) {
  if (!Number.isSafeInteger(expectedFactsVersion) || expectedFactsVersion < 0) throw new MalformedRevisionResponse("factsVersion");
  if (operationId.length === 0 || operationId.length > 200 || operationId.startsWith("transition:")) {
    throw new MalformedRevisionResponse("operation id");
  }
  if (!EVIDENCE_CURRENCIES.includes(input.evidenceCurrency)) throw new MalformedRevisionResponse("evidenceCurrency");
  if (input.citations.length > VISIBILITY_LIMITS.citations) throw new MalformedRevisionResponse("too many citations");
  if (!input.restricted && input.citations.length === 0) {
    throw new MalformedRevisionResponse("a public revision needs at least one citation");
  }
  const L = VISIBILITY_LIMITS;
  const optional = (v: string | null | undefined, name: string) =>
    v === null || v === undefined || v.trim() === "" ? null : bounded(v, L.disclosure, name, true);
  return {
    expectedFactsVersion,
    operationId,
    restricted: input.restricted,
    superseded: input.superseded,
    expiresAt: optionalDate(input.expiresAt, "expiresAt"),
    freshnessRequired: input.freshnessRequired,
    evidenceCurrency: input.evidenceCurrency,
    historicalLabelPresent: input.historicalLabelPresent,
    citations: input.citations.map((c) => ({
      citationLabel: bounded(c.citationLabel, L.label, "citation label", false),
      title: bounded(c.title, L.title, "title", false),
      quote: bounded(c.quote, L.quote, "quote", true),
      sourceName: bounded(c.sourceName, L.source, "source name", false),
      freshnessLabel: bounded(c.freshnessLabel, L.label, "freshness label", false),
      asOf: optionalDate(c.asOf, "asOf"),
      effectiveDate: optionalDate(c.effectiveDate, "effectiveDate"),
      contradictionDisclosure: optional(c.contradictionDisclosure, "contradiction disclosure"),
      uncertaintyLimit: optional(c.uncertaintyLimit, "uncertainty limit"),
    })),
  };
}

export async function setVisibility(
  id: string,
  input: VisibilityInput,
  expectedFactsVersion: number,
  operationId: string,
): Promise<{ replayed: boolean; revision: PublicationRevision }> {
  const body = buildVisibilityBody(input, expectedFactsVersion, operationId);
  return parseTransitionResponse(await apiPutBody<unknown, typeof body>(visibilityPath(id), body));
}

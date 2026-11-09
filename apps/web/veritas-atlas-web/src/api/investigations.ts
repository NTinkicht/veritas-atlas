import { apiGet } from "./http";

// VER-P0-17: read-only client for the tenant-scoped investigation workspace API
// (VER-P0-10, /api/v1/tenant-investigations). No seeded or cached fallback: what is shown is
// exactly what the API returned, and a malformed response is rejected whole (fail closed).

export type Investigation = {
  id: string;
  title: string;
  createdBySubject: string;
  createdAtUtc: string;
};

export type CollectedEvidenceItem = {
  id: string;
  evidenceVerificationId: string;
  note: string | null;
  collectedBySubject: string;
  createdAtUtc: string;
};

export const INVESTIGATIONS_PATH = "/api/v1/tenant-investigations";
const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

export class MalformedInvestigationResponse extends Error {
  constructor(detail: string) {
    super(`The investigation service returned an unexpected response (${detail}).`);
    this.name = "MalformedInvestigationResponse";
  }
}

export function evidenceItemsPath(investigationId: string): string {
  if (!GUID.test(investigationId)) throw new MalformedInvestigationResponse("investigation id is not a GUID");
  return `${INVESTIGATIONS_PATH}/${investigationId}/evidence-items`;
}

function items(raw: unknown): Record<string, unknown>[] {
  if (typeof raw !== "object" || raw === null) throw new MalformedInvestigationResponse("not an object");
  const list = (raw as Record<string, unknown>).items;
  if (!Array.isArray(list)) throw new MalformedInvestigationResponse("items missing");
  return list.map((item, index) => {
    if (typeof item !== "object" || item === null) throw new MalformedInvestigationResponse(`item ${index}`);
    return item as Record<string, unknown>;
  });
}

function text(record: Record<string, unknown>, key: string, index: number): string {
  const value = record[key];
  if (typeof value !== "string" || value.length === 0) throw new MalformedInvestigationResponse(`item ${index} ${key}`);
  return value;
}

function guid(record: Record<string, unknown>, key: string, index: number): string {
  const value = text(record, key, index);
  if (!GUID.test(value)) throw new MalformedInvestigationResponse(`item ${index} ${key}`);
  return value;
}

export function parseInvestigations(raw: unknown): Investigation[] {
  return items(raw).map((r, i) => ({
    id: guid(r, "id", i),
    title: text(r, "title", i),
    createdBySubject: text(r, "createdBySubject", i),
    createdAtUtc: text(r, "createdAtUtc", i),
  }));
}

export function parseEvidenceItems(raw: unknown): CollectedEvidenceItem[] {
  return items(raw).map((r, i) => {
    const note = r.note;
    if (note !== null && note !== undefined && typeof note !== "string") {
      throw new MalformedInvestigationResponse(`item ${i} note`);
    }
    return {
      id: guid(r, "id", i),
      evidenceVerificationId: guid(r, "evidenceVerificationId", i),
      note: typeof note === "string" && note.length > 0 ? note : null,
      collectedBySubject: text(r, "collectedBySubject", i),
      createdAtUtc: text(r, "createdAtUtc", i),
    };
  });
}

export type InvestigationFailureKind = "unauthenticated" | "forbidden" | "not-found" | "offline" | "server" | "malformed";
export type InvestigationFailure = { kind: InvestigationFailureKind; message: string };

export function classifyInvestigationFailure(error: unknown, online: boolean = true): InvestigationFailure {
  if (error instanceof MalformedInvestigationResponse) return { kind: "malformed", message: error.message };
  const status = typeof error === "object" && error !== null ? (error as { status?: unknown }).status : undefined;
  if (status === 401) return { kind: "unauthenticated", message: "Your session has ended or is not valid. Sign in again." };
  if (status === 403) return { kind: "forbidden", message: "Your account is not permitted to read investigations for an organization." };
  if (status === 404) return { kind: "not-found", message: "That investigation was not found or is not shared with you." };
  if (typeof status === "number") {
    return { kind: "server", message: `The investigation service failed (HTTP ${status}). Nothing is shown rather than a guess.` };
  }
  return {
    kind: "offline",
    message: online ? "The investigation service could not be reached. Check your connection and try again." : "You appear to be offline. Reconnect and try again.",
  };
}

export async function listInvestigations(): Promise<Investigation[]> {
  return parseInvestigations(await apiGet<unknown>(INVESTIGATIONS_PATH));
}

export async function listEvidenceItems(investigationId: string): Promise<CollectedEvidenceItem[]> {
  return parseEvidenceItems(await apiGet<unknown>(evidenceItemsPath(investigationId)));
}

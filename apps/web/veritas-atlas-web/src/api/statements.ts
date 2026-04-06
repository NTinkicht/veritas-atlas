import { apiGet } from "./client";

export type StatementItem = {
  id: string;
  text: string;
  topic: string | null;
  predicate: string | null;
  object: string | null;
  polarity: string;
  status: string;
  evidenceId?: string | null;
  createdAt: string;
};

export type StatementDetail = {
  id: string;
  text: string;
  topic: string | null;
  predicate: string | null;
  object: string | null;
  polarity: string;
  status: string;
  evidenceId: string | null;
  personId: string | null;
  createdAt: string;
};

export type StatementsResponse = {
  total: number;
  page: number;
  pageSize: number;
  items: StatementItem[];
};

export async function getStatements(): Promise<StatementsResponse> {
  return apiGet<StatementsResponse>("/api/v1/statements?page=1&pageSize=50");
}

export async function getStatementById(id: string): Promise<StatementDetail> {
  return apiGet<StatementDetail>(`/api/v1/statements/${id}`);
}

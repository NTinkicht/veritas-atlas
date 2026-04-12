import type { StatementItem } from "./contracts";
import { apiGet } from "./http";

export async function getStatementById(id: string): Promise<StatementItem> {
  return apiGet<StatementItem>(`/api/v1/statements/${id}`);
}
export type { StatementItem } from "./contracts";

export type GetStatementsResponse = {
  items: StatementItem[];
  total: number;
};

export async function getStatements(): Promise<GetStatementsResponse> {
  return { items: [], total: 0 };
}
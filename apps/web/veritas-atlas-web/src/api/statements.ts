import type { StatementItem } from "./contracts";
import { apiGet } from "./http";

export async function getStatementById(id: string): Promise<StatementItem> {
  return apiGet<StatementItem>(`/api/v1/statements/${id}`);
}
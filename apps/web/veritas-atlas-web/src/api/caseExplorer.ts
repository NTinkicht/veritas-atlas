export type CaseExplorerItem = {
  id: string;
  status: string;
  createdAtUtc: string;
  createdBy?: string | null;
};

export type CaseExplorerResponse = {
  items: CaseExplorerItem[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
};

export async function getCaseExplorer(page = 1, pageSize = 50): Promise<CaseExplorerResponse> {
  const response = await fetch(`/api/v1/cases?page=${page}&pageSize=${pageSize}`);

  if (!response.ok) {
    const text = await response.text();
    throw new Error(text || `HTTP ${response.status}`);
  }

  return response.json() as Promise<CaseExplorerResponse>;
}
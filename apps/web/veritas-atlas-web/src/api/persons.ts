import { apiGet } from "./client";

export type PersonItem = {
  id: string;
  displayName: string;
  description: string | null;
  nationality: string | null;
  createdAtUtc: string;
  updatedAtUtc: string;
  aliases: string[];
};

export type PersonsResponse = {
  items: PersonItem[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
};

export async function getPersons(): Promise<PersonsResponse> {
  return apiGet<PersonsResponse>("/api/v1/persons?page=1&pageSize=100");
}

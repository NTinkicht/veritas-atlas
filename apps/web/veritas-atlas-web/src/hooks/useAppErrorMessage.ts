export function useAppErrorMessage(error: unknown): string | null {
  if (!error) return null;

  if (typeof error === "string") return error;

  if (typeof error === "object" && error !== null) {
    const record = error as Record<string, unknown>;
    if (typeof record.message === "string" && record.message.trim()) {
      return record.message;
    }
  }

  return "Something went wrong.";
}
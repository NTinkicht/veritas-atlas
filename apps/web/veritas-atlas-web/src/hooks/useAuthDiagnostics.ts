import { useQuery } from "@tanstack/react-query";
import { getProtectedAuthDiagnostics, getPublicAuthDiagnostics } from "../api/authDiagnostics";

export function usePublicAuthDiagnostics() {
  return useQuery({
    queryKey: ["auth-diagnostics-public"],
    queryFn: () => getPublicAuthDiagnostics(),
  });
}

export function useProtectedAuthDiagnostics() {
  return useQuery({
    queryKey: ["auth-diagnostics-protected"],
    queryFn: () => getProtectedAuthDiagnostics(),
    retry: false,
  });
}
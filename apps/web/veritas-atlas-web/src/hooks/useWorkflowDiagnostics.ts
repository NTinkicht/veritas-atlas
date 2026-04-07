import { useQuery } from "@tanstack/react-query";
import {
  getWorkflowDiagnosticsRoutes,
  getWorkflowDiagnosticsSummary,
} from "../api/workflowDiagnostics";

export function useWorkflowDiagnosticsSummary() {
  return useQuery({
    queryKey: ["workflow-diagnostics-summary"],
    queryFn: () => getWorkflowDiagnosticsSummary(),
  });
}

export function useWorkflowDiagnosticsRoutes() {
  return useQuery({
    queryKey: ["workflow-diagnostics-routes"],
    queryFn: () => getWorkflowDiagnosticsRoutes(),
  });
}
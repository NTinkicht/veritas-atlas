import type { QuickLinkItem } from "../components/QuickLinkGrid";

export function useOperationalLinks(): QuickLinkItem[] {
  return [
    {
      href: "/dashboard",
      label: "Dashboard",
      description: "Current stabilized dashboard entry point.",
    },
    {
      href: "/cases",
      label: "Cases",
      description: "Browse and inspect cases.",
    },
    {
      href: "/claims",
      label: "Claims",
      description: "Review claim entities and linked actions.",
    },
    {
      href: "/contradictions",
      label: "Contradictions",
      description: "Inspect contradiction queues and details.",
    },
    {
      href: "/workflow-audit",
      label: "Workflow Audit",
      description: "See transition history and decisions.",
    },
    {
      href: "/persistence",
      label: "Persistence",
      description: "Inspect snapshot and reset controls.",
    },
  ];
}
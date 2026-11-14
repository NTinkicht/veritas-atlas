import { isMounted } from "../routeAudit";

const NAV_LINKS = [
  { href: "/", label: "Home" },
  { href: "/dashboard", label: "Dashboard" },
  { href: "/evidence-search", label: "Evidence Search" },
  { href: "/investigations", label: "Investigations" },
  { href: "/publication-revisions", label: "Publication Revisions" },
  { href: "/claim-confidence", label: "Claim Confidence" },
  { href: "/citation-inspector", label: "Citation Inspector" },
  { href: "/claim-relations", label: "Claim Relations" },
  { href: "/cases", label: "Cases" },
  { href: "/claims", label: "Claims" },
  { href: "/contradictions", label: "Contradictions" },
  { href: "/workflow-audit", label: "Workflow Audit" },
  { href: "/persistence", label: "Persistence" },
  { href: "/operational-actions", label: "Operational Actions" },
  { href: "/login", label: "Login" }
];

export function useFrontendNav() {
  return NAV_LINKS.filter((link) => isMounted(link.href, import.meta.env.DEV));
}

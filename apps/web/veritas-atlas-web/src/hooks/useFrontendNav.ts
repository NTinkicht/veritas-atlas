import { isMounted } from "../routeAudit";

const NAV_LINKS = [
  { href: "/", label: "Home" },
  { href: "/dashboard", label: "Dashboard" },
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

import { AppSurface } from "../components/AppSurface";
import { useFrontendNav } from "../hooks/useFrontendNav";

export function CaseScoreboardPage() {
  const links = useFrontendNav();

  return (
    <AppSurface
      title="Case Scoreboard"
      subtitle="Review current case status totals and operational workload."
      links={links}
    />
  );
}
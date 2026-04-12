import { AppSurface } from "../components/AppSurface";
import { useFrontendNav } from "../hooks/useFrontendNav";

export function AgentRunsMonitorPage() {
  const links = useFrontendNav();

  return (
    <AppSurface
      title="A ge nt Ru ns Mo ni to r"
      subtitle="Stable production shell."
      links={links}
    />
  );
}
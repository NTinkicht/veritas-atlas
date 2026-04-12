import { AppSurface } from "../components/AppSurface";
import { useFrontendNav } from "../hooks/useFrontendNav";

export function OperationsHubPage() {
  const links = useFrontendNav();

  return (
    <AppSurface
      title="O pe ra ti on sH ub"
      subtitle="Stable production shell."
      links={links}
    />
  );
}
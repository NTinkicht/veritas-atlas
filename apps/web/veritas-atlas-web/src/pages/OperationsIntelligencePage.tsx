import { AppSurface } from "../components/AppSurface";
import { useFrontendNav } from "../hooks/useFrontendNav";

export function OperationsIntelligencePage() {
  const links = useFrontendNav();

  return (
    <AppSurface
      title="O pe ra ti on sI nt el li ge nc e"
      subtitle="Stable production shell."
      links={links}
    />
  );
}
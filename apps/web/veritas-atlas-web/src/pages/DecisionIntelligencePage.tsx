import { AppSurface } from "../components/AppSurface";
import { useFrontendNav } from "../hooks/useFrontendNav";

export function DecisionIntelligencePage() {
  const links = useFrontendNav();

  return (
    <AppSurface
      title="D ec is io nI nt el li ge nc e"
      subtitle="Stable production shell."
      links={links}
    />
  );
}
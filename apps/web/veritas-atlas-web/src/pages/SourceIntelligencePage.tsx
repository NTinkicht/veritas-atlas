import { AppSurface } from "../components/AppSurface";
import { useFrontendNav } from "../hooks/useFrontendNav";

export function SourceIntelligencePage() {
  const links = useFrontendNav();

  return (
    <AppSurface
      title="S ou rc eI nt el li ge nc e"
      subtitle="Stable production shell."
      links={links}
    />
  );
}
import { AppSurface } from "../components/AppSurface";
import { useFrontendNav } from "../hooks/useFrontendNav";

export function EvidencePage() {
  const links = useFrontendNav();

  return (
    <AppSurface
      title="E vi de nc e"
      subtitle="Stable production shell."
      links={links}
    />
  );
}
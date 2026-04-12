import { AppSurface } from "../components/AppSurface";
import { useFrontendNav } from "../hooks/useFrontendNav";

export function ReadinessRadarWorkspacePage() {
  const links = useFrontendNav();

  return (
    <AppSurface
      title="R ea di ne ss Ra da rW or ks pa ce"
      subtitle="Stable production shell."
      links={links}
    />
  );
}
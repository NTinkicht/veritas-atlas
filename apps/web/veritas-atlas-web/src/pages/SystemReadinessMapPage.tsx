import { AppSurface } from "../components/AppSurface";
import { useFrontendNav } from "../hooks/useFrontendNav";

export function SystemReadinessMapPage() {
  const links = useFrontendNav();

  return (
    <AppSurface
      title="S ys te mR ea di ne ss Ma p"
      subtitle="Stable production shell."
      links={links}
    />
  );
}
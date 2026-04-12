import { AppSurface } from "../components/AppSurface";
import { useFrontendNav } from "../hooks/useFrontendNav";

export function SeededLifecycleRunnerPage() {
  const links = useFrontendNav();

  return (
    <AppSurface
      title="S ee de dL if ec yc le Ru nn er"
      subtitle="Stable production shell."
      links={links}
    />
  );
}
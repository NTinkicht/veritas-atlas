import { AppSurface } from "../components/AppSurface";
import { useFrontendNav } from "../hooks/useFrontendNav";

export function OpsFinalizationWorkspacePage() {
  const links = useFrontendNav();

  return (
    <AppSurface
      title="O ps Fi na li za ti on Wo rk sp ac e"
      subtitle="Stable production shell."
      links={links}
    />
  );
}
import { AppSurface } from "../components/AppSurface";
import { useFrontendNav } from "../hooks/useFrontendNav";

export function CaseFlowMapPage() {
  const links = useFrontendNav();

  return (
    <AppSurface
      title="C as eF lo wM ap"
      subtitle="Stable production shell."
      links={links}
    />
  );
}
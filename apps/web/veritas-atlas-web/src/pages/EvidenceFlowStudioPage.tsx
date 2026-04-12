import { AppSurface } from "../components/AppSurface";
import { useFrontendNav } from "../hooks/useFrontendNav";

export function EvidenceFlowStudioPage() {
  const links = useFrontendNav();

  return (
    <AppSurface
      title="E vi de nc eF lo wS tu di o"
      subtitle="Stable production shell."
      links={links}
    />
  );
}
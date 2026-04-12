import { AppSurface } from "../components/AppSurface";
import { useFrontendNav } from "../hooks/useFrontendNav";

export function EvidenceTracePage() {
  const links = useFrontendNav();

  return (
    <AppSurface
      title="E vi de nc eT ra ce"
      subtitle="Stable production shell."
      links={links}
    />
  );
}
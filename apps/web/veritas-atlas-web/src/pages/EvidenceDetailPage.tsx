import { AppSurface } from "../components/AppSurface";
import { useFrontendNav } from "../hooks/useFrontendNav";

export function EvidenceDetailPage() {
  const links = useFrontendNav();

  return (
    <AppSurface
      title="E vi de nc eD et ai l"
      subtitle="Stable production shell."
      links={links}
    />
  );
}
import { AppSurface } from "../components/AppSurface";
import { useFrontendNav } from "../hooks/useFrontendNav";

export function SourceDetailPage() {
  const links = useFrontendNav();

  return (
    <AppSurface
      title="S ou rc eD et ai l"
      subtitle="Stable production shell."
      links={links}
    />
  );
}
import { AppSurface } from "../components/AppSurface";
import { useFrontendNav } from "../hooks/useFrontendNav";

export function SourcesPage() {
  const links = useFrontendNav();

  return (
    <AppSurface
      title="S ou rc es"
      subtitle="Stable production shell."
      links={links}
    />
  );
}
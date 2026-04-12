import { AppSurface } from "../components/AppSurface";
import { useFrontendNav } from "../hooks/useFrontendNav";

export function AppSurfaceCatalogPage() {
  const links = useFrontendNav();

  return (
    <AppSurface
      title="A pp Su rf ac eC at al og"
      subtitle="Stable production shell."
      links={links}
    />
  );
}
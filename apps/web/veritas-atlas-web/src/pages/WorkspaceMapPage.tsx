import { AppSurface } from "../components/AppSurface";
import { useFrontendNav } from "../hooks/useFrontendNav";

export function WorkspaceMapPage() {
  const links = useFrontendNav();

  return (
    <AppSurface
      title="W or ks pa ce Ma p"
      subtitle="Stable production shell."
      links={links}
    />
  );
}
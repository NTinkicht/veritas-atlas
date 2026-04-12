import { AppSurface } from "../components/AppSurface";
import { useFrontendNav } from "../hooks/useFrontendNav";

export function Phase16WorkspaceIndexPage() {
  const links = useFrontendNav();

  return (
    <AppSurface
      title="P ha se16 Wo rk sp ac eI nd ex"
      subtitle="Stable production shell."
      links={links}
    />
  );
}
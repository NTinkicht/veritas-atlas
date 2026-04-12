import { AppSurface } from "../components/AppSurface";
import { useFrontendNav } from "../hooks/useFrontendNav";

export function Phase18WorkspaceStarterPage() {
  const links = useFrontendNav();

  return (
    <AppSurface
      title="P ha se18 Wo rk sp ac eS ta rt er"
      subtitle="Stable production shell."
      links={links}
    />
  );
}
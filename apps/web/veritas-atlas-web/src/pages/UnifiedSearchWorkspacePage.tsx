import { AppSurface } from "../components/AppSurface";
import { useFrontendNav } from "../hooks/useFrontendNav";

export function UnifiedSearchWorkspacePage() {
  const links = useFrontendNav();

  return (
    <AppSurface
      title="U ni fi ed Se ar ch Wo rk sp ac e"
      subtitle="Stable production shell."
      links={links}
    />
  );
}
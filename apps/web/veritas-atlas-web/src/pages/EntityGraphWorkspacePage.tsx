import { AppSurface } from "../components/AppSurface";
import { useFrontendNav } from "../hooks/useFrontendNav";

export function EntityGraphWorkspacePage() {
  const links = useFrontendNav();

  return (
    <AppSurface
      title="E nt it yG ra ph Wo rk sp ac e"
      subtitle="Stable production shell."
      links={links}
    />
  );
}
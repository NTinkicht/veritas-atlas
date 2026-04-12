import { AppSurface } from "../components/AppSurface";
import { useFrontendNav } from "../hooks/useFrontendNav";

export function AdminControlTowerPage() {
  const links = useFrontendNav();

  return (
    <AppSurface
      title="A dm in Co nt ro lT ow er"
      subtitle="Stable production shell."
      links={links}
    />
  );
}
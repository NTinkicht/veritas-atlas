import { AppSurface } from "../components/AppSurface";
import { useFrontendNav } from "../hooks/useFrontendNav";

export function Phase14ControlCenterPage() {
  const links = useFrontendNav();

  return (
    <AppSurface
      title="P ha se14 Co nt ro lC en te r"
      subtitle="Stable production shell."
      links={links}
    />
  );
}
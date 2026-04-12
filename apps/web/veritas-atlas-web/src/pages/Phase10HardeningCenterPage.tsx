import { AppSurface } from "../components/AppSurface";
import { useFrontendNav } from "../hooks/useFrontendNav";

export function Phase10HardeningCenterPage() {
  const links = useFrontendNav();

  return (
    <AppSurface
      title="P ha se10 Ha rd en in gC en te r"
      subtitle="Stable production shell."
      links={links}
    />
  );
}
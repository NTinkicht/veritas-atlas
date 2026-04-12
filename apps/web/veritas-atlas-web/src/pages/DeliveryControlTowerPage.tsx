import { AppSurface } from "../components/AppSurface";
import { useFrontendNav } from "../hooks/useFrontendNav";

export function DeliveryControlTowerPage() {
  const links = useFrontendNav();

  return (
    <AppSurface
      title="D el iv er yC on tr ol To we r"
      subtitle="Stable production shell."
      links={links}
    />
  );
}
import { AppSurface } from "../components/AppSurface";
import { useFrontendNav } from "../hooks/useFrontendNav";

export function DeliveryCloseoutWorkspacePage() {
  const links = useFrontendNav();

  return (
    <AppSurface
      title="D el iv er yC lo se ou tW or ks pa ce"
      subtitle="Stable production shell."
      links={links}
    />
  );
}
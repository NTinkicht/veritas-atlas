import { AppSurface } from "../components/AppSurface";
import { useFrontendNav } from "../hooks/useFrontendNav";

export function DocumentDetailPage() {
  const links = useFrontendNav();

  return (
    <AppSurface
      title="D oc um en tD et ai l"
      subtitle="Stable production shell."
      links={links}
    />
  );
}
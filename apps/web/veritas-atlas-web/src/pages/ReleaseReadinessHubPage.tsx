import { AppSurface } from "../components/AppSurface";
import { useFrontendNav } from "../hooks/useFrontendNav";

export function ReleaseReadinessHubPage() {
  const links = useFrontendNav();

  return (
    <AppSurface
      title="R el ea se Re ad in es sH ub"
      subtitle="Stable production shell."
      links={links}
    />
  );
}
import { AppSurface } from "../components/AppSurface";
import { useFrontendNav } from "../hooks/useFrontendNav";

export function IngestionWorkspacePage() {
  const links = useFrontendNav();

  return (
    <AppSurface
      title="I ng es ti on Wo rk sp ac e"
      subtitle="Stable production shell."
      links={links}
    />
  );
}
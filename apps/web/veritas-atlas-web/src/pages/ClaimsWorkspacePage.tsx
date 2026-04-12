import { AppSurface } from "../components/AppSurface";
import { useFrontendNav } from "../hooks/useFrontendNav";

export function ClaimsWorkspacePage() {
  const links = useFrontendNav();

  return (
    <AppSurface
      title="C la im sW or ks pa ce"
      subtitle="Stable production shell."
      links={links}
    />
  );
}
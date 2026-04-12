import { AppSurface } from "../components/AppSurface";
import { useFrontendNav } from "../hooks/useFrontendNav";

export function ExecutiveReadoutWorkspacePage() {
  const links = useFrontendNav();

  return (
    <AppSurface
      title="E xe cu ti ve Re ad ou tW or ks pa ce"
      subtitle="Stable production shell."
      links={links}
    />
  );
}
import { AppSurface } from "../components/AppSurface";
import { useFrontendNav } from "../hooks/useFrontendNav";

export function ReviewWorkspacePage() {
  const links = useFrontendNav();

  return (
    <AppSurface
      title="R ev ie wW or ks pa ce"
      subtitle="Stable production shell."
      links={links}
    />
  );
}
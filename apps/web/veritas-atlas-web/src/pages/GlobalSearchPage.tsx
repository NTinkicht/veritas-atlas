import { AppSurface } from "../components/AppSurface";
import { useFrontendNav } from "../hooks/useFrontendNav";

export function GlobalSearchPage() {
  const links = useFrontendNav();

  return (
    <AppSurface
      title="G lo ba lS ea rc h"
      subtitle="Stable production shell."
      links={links}
    />
  );
}
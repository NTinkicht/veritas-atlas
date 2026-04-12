import { AppSurface } from "../components/AppSurface";
import { useFrontendNav } from "../hooks/useFrontendNav";

export function NavigationIndexPage() {
  const links = useFrontendNav();

  return (
    <AppSurface
      title="N av ig at io nI nd ex"
      subtitle="Stable production shell."
      links={links}
    />
  );
}
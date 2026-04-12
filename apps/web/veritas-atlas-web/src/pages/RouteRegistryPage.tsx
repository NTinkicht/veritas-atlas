import { AppSurface } from "../components/AppSurface";
import { useFrontendNav } from "../hooks/useFrontendNav";

export function RouteRegistryPage() {
  const links = useFrontendNav();

  return (
    <AppSurface
      title="R ou te Re gi st ry"
      subtitle="Stable production shell."
      links={links}
    />
  );
}
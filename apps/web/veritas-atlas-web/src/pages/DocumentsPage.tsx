import { AppSurface } from "../components/AppSurface";
import { useFrontendNav } from "../hooks/useFrontendNav";

export function DocumentsPage() {
  const links = useFrontendNav();

  return (
    <AppSurface
      title="D oc um en ts"
      subtitle="Stable production shell."
      links={links}
    />
  );
}
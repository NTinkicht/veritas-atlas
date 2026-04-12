import { AppSurface } from "../components/AppSurface";
import { useFrontendNav } from "../hooks/useFrontendNav";

export function AuthDiagnosticsPage() {
  const links = useFrontendNav();

  return (
    <AppSurface
      title="A ut hD ia gn os ti cs"
      subtitle="Stable production shell."
      links={links}
    />
  );
}
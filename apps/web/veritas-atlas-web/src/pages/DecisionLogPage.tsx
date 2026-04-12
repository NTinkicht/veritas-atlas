import { AppSurface } from "../components/AppSurface";
import { useFrontendNav } from "../hooks/useFrontendNav";

export function DecisionLogPage() {
  const links = useFrontendNav();

  return (
    <AppSurface
      title="D ec is io nL og"
      subtitle="Stable production shell."
      links={links}
    />
  );
}
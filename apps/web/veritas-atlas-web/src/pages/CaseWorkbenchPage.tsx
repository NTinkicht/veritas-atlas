import { AppSurface } from "../components/AppSurface";
import { useFrontendNav } from "../hooks/useFrontendNav";

export function CaseWorkbenchPage() {
  const links = useFrontendNav();

  return (
    <AppSurface
      title="C as eW or kb en ch"
      subtitle="Stable production shell."
      links={links}
    />
  );
}
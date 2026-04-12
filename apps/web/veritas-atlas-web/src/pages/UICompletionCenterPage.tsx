import { AppSurface } from "../components/AppSurface";
import { useFrontendNav } from "../hooks/useFrontendNav";

export function UICompletionCenterPage() {
  const links = useFrontendNav();

  return (
    <AppSurface
      title="U IC om pl et io nC en te r"
      subtitle="Stable production shell."
      links={links}
    />
  );
}
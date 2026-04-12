import { AppSurface } from "../components/AppSurface";
import { useFrontendNav } from "../hooks/useFrontendNav";

export function LifecycleSeedPage() {
  const links = useFrontendNav();

  return (
    <AppSurface
      title="L if ec yc le Se ed"
      subtitle="Stable production shell."
      links={links}
    />
  );
}
import { AppSurface } from "../components/AppSurface";
import { useFrontendNav } from "../hooks/useFrontendNav";

export function PlatformAtlasPage() {
  const links = useFrontendNav();

  return (
    <AppSurface
      title="P la tf or mA tl as"
      subtitle="Stable production shell."
      links={links}
    />
  );
}
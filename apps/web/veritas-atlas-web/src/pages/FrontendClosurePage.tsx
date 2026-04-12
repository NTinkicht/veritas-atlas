import { AppSurface } from "../components/AppSurface";
import { useFrontendNav } from "../hooks/useFrontendNav";

export function FrontendClosurePage() {
  const links = useFrontendNav();

  return (
    <AppSurface
      title="F ro nt en dC lo su re"
      subtitle="Stable production shell."
      links={links}
    />
  );
}
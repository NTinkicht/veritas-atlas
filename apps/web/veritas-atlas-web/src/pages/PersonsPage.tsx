import { AppSurface } from "../components/AppSurface";
import { useFrontendNav } from "../hooks/useFrontendNav";

export function PersonsPage() {
  const links = useFrontendNav();

  return (
    <AppSurface
      title="P er so ns"
      subtitle="Stable production shell."
      links={links}
    />
  );
}
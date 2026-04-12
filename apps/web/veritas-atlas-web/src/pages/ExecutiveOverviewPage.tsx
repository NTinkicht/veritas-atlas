import { AppSurface } from "../components/AppSurface";
import { useFrontendNav } from "../hooks/useFrontendNav";

export function ExecutiveOverviewPage() {
  const links = useFrontendNav();

  return (
    <AppSurface
      title="E xe cu ti ve Ov er vi ew"
      subtitle="Stable production shell."
      links={links}
    />
  );
}
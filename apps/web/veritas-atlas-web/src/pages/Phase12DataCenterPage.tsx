import { AppSurface } from "../components/AppSurface";
import { useFrontendNav } from "../hooks/useFrontendNav";

export function Phase12DataCenterPage() {
  const links = useFrontendNav();

  return (
    <AppSurface
      title="P ha se12 Da ta Ce nt er"
      subtitle="Stable production shell."
      links={links}
    />
  );
}
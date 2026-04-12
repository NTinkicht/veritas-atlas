import { AppSurface } from "../components/AppSurface";
import { useFrontendNav } from "../hooks/useFrontendNav";

export function Phase9IntegrityCenterPage() {
  const links = useFrontendNav();

  return (
    <AppSurface
      title="P ha se9 In te gr it yC en te r"
      subtitle="Stable production shell."
      links={links}
    />
  );
}
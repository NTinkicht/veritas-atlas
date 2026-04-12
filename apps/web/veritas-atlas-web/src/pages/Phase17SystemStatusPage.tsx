import { AppSurface } from "../components/AppSurface";
import { useFrontendNav } from "../hooks/useFrontendNav";

export function Phase17SystemStatusPage() {
  const links = useFrontendNav();

  return (
    <AppSurface
      title="P ha se17 Sy st em St at us"
      subtitle="Stable production shell."
      links={links}
    />
  );
}
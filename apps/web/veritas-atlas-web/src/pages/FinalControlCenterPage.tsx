import { AppSurface } from "../components/AppSurface";
import { useFrontendNav } from "../hooks/useFrontendNav";

export function FinalControlCenterPage() {
  const links = useFrontendNav();

  return (
    <AppSurface
      title="F in al Co nt ro lC en te r"
      subtitle="Stable production shell."
      links={links}
    />
  );
}
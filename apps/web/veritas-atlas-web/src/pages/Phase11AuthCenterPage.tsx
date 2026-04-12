import { AppSurface } from "../components/AppSurface";
import { useFrontendNav } from "../hooks/useFrontendNav";

export function Phase11AuthCenterPage() {
  const links = useFrontendNav();

  return (
    <AppSurface
      title="P ha se11 Au th Ce nt er"
      subtitle="Stable production shell."
      links={links}
    />
  );
}
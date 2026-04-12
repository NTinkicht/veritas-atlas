import { AppSurface } from "../components/AppSurface";
import { useFrontendNav } from "../hooks/useFrontendNav";

export function RolePolicyPage() {
  const links = useFrontendNav();

  return (
    <AppSurface
      title="R ol eP ol ic y"
      subtitle="Stable production shell."
      links={links}
    />
  );
}
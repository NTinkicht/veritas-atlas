import { AppSurface } from "../components/AppSurface";
import { useFrontendNav } from "../hooks/useFrontendNav";

export function AuthMePage() {
  const links = useFrontendNav();

  return (
    <AppSurface
      title="A ut hM e"
      subtitle="Stable production shell."
      links={links}
    />
  );
}
import { AppSurface } from "../components/AppSurface";
import { useFrontendNav } from "../hooks/useFrontendNav";

export function WorkflowValidationPage() {
  const links = useFrontendNav();

  return (
    <AppSurface
      title="W or kf lo wV al id at io n"
      subtitle="Stable production shell."
      links={links}
    />
  );
}
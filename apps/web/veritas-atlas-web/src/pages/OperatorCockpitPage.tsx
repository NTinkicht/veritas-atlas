import { AppSurface } from "../components/AppSurface";
import { useFrontendNav } from "../hooks/useFrontendNav";

export function OperatorCockpitPage() {
  const links = useFrontendNav();

  return (
    <AppSurface
      title="O pe ra to rC oc kp it"
      subtitle="Stable production shell."
      links={links}
    />
  );
}
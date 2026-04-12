import { AppSurface } from "../components/AppSurface";
import { useFrontendNav } from "../hooks/useFrontendNav";

export function GovernanceConsolePage() {
  const links = useFrontendNav();

  return (
    <AppSurface
      title="G ov er na nc eC on so le"
      subtitle="Stable production shell."
      links={links}
    />
  );
}
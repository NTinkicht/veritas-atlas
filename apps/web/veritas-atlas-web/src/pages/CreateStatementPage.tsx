import { AppSurface } from "../components/AppSurface";
import { useFrontendNav } from "../hooks/useFrontendNav";

export function CreateStatementPage() {
  const links = useFrontendNav();

  return (
    <AppSurface
      title="C re at eS ta te me nt"
      subtitle="Stable production shell."
      links={links}
    />
  );
}
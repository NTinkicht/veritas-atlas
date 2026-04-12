import { AppSurface } from "../components/AppSurface";
import { useFrontendNav } from "../hooks/useFrontendNav";

export function CreateDocumentPage() {
  const links = useFrontendNav();

  return (
    <AppSurface
      title="C re at eD oc um en t"
      subtitle="Stable production shell."
      links={links}
    />
  );
}
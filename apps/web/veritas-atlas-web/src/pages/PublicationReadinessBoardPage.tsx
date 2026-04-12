import { AppSurface } from "../components/AppSurface";
import { useFrontendNav } from "../hooks/useFrontendNav";

export function PublicationReadinessBoardPage() {
  const links = useFrontendNav();

  return (
    <AppSurface
      title="P ub li ca ti on Re ad in es sB oa rd"
      subtitle="Stable production shell."
      links={links}
    />
  );
}
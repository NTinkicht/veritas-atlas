import { AppSurface } from "../components/AppSurface";
import { useFrontendNav } from "../hooks/useFrontendNav";

export function ReviewDecisionBoardPage() {
  const links = useFrontendNav();

  return (
    <AppSurface
      title="R ev ie wD ec is io nB oa rd"
      subtitle="Stable production shell."
      links={links}
    />
  );
}
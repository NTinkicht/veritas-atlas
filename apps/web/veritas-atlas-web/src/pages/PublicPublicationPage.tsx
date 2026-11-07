import { useEffect, useState } from "react";
import { useParams } from "react-router-dom";
import { getPublicPublication } from "../api/publicPublication";
import { PublicReaderView, type PublicReaderState } from "../components/PublicReaderView";

export function PublicPublicationPage() {
  const { id = "" } = useParams();
  return <PublicPublicationLoader key={id} id={id} />;
}

function PublicPublicationLoader({ id }: { id: string }) {
  const [state, setState] = useState<PublicReaderState>({ kind: "loading" });

  useEffect(() => {
    let live = true;
    void getPublicPublication(id).then((r) => {
      if (live) setState(r);
    });
    return () => {
      live = false;
    };
  }, [id]);

  return (
    <main style={{ maxWidth: 720, margin: "0 auto", padding: 16 }}>
      <PublicReaderView state={state} />
    </main>
  );
}

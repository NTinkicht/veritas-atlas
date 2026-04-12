import type { ReactNode } from "react";
import { Navigate, useLocation } from "react-router-dom";
import { hasAccessToken } from "../api/httpAuth";

type RequireAuthProps = {
  children: ReactNode;
};

export function RequireAuth({ children }: RequireAuthProps) {
  const location = useLocation();

  if (!hasAccessToken()) {
    const returnTo = encodeURIComponent(`${location.pathname}${location.search}${location.hash}`);
    return <Navigate to={`/login?returnTo=${returnTo}`} replace />;
  }

  return <>{children}</>;
}
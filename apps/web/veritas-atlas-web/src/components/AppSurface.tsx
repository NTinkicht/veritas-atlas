import type { ReactNode } from "react";
import { Link, useLocation } from "react-router-dom";
import { useFrontendNav } from "../hooks/useFrontendNav";
import { UtilityHeader } from "./UtilityHeader";

type NavLinkItem = {
  href: string;
  label: string;
};

type AppSurfaceProps = {
  title: string;
  subtitle?: string;
  children?: ReactNode;
  links?: NavLinkItem[];
};

export function AppSurface({ title, subtitle, children, links }: AppSurfaceProps) {
  const location = useLocation();
  const defaultLinks = useFrontendNav();
  const navLinks = links ?? defaultLinks;

  return (
    <div className="app-shell">
      <a className="skip-link" href="#main-content">Skip to main content</a>
      <aside className="app-sidebar">
        <div className="app-brand">
          <p className="app-brand-name">Veritas Atlas</p>
          <p>Evidence-led truth intelligence platform.</p>
        </div>

        <nav className="app-nav" aria-label="Primary">
          {navLinks.map((item) => {
            const isActive =
              location.pathname === item.href ||
              (item.href !== "/" && location.pathname.startsWith(item.href));

            return (
              <Link
                key={item.href}
                className="app-nav-link"
                to={item.href}
                aria-current={isActive ? "page" : undefined}
                style={
                  isActive
                    ? {
                        background: "rgba(96,165,250,.12)",
                        borderColor: "rgba(96,165,250,.24)",
                        color: "#ffffff",
                      }
                    : undefined
                }
              >
                {item.label}
              </Link>
            );
          })}
        </nav>
      </aside>

      <main className="app-main" id="main-content" tabIndex={-1}>
        <div className="page">
          <section className="hero-card">
            <h1 className="hero-title">{title}</h1>
            {subtitle ? <p className="hero-subtitle">{subtitle}</p> : null}
          </section>
          <UtilityHeader />
          {children}
        </div>
      </main>
    </div>
  );
}
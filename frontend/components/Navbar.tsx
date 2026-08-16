'use client';

import Link from 'next/link';
import { usePathname } from 'next/navigation';
import { useEffect, useState } from 'react';
import { useAuth } from '@/contexts/AuthContext';

export function Navbar() {
  const pathname = usePathname();
  const [mobileOpen, setMobileOpen] = useState(false);
  const { isAuthenticated, logout } = useAuth();

  const isActive = (path: string) => {
    if (path === '/dashboard') {
      return pathname === '/dashboard' || pathname.startsWith('/dashboard/');
    }
    if (path === '/activities') {
      return pathname === '/activities';
    }
    return pathname === path;
  };

  const navLinkClasses = (path: string) => {
    const base =
      'block px-4 py-2 text-sm font-medium rounded-tempo transition-colors';
    const active = 'bg-volt text-on-volt';
    const inactive =
      'text-muted hover:text-ink hover:bg-canvas';

    return `${base} ${isActive(path) ? active : inactive}`;
  };

  useEffect(() => {
    setMobileOpen(false);
  }, [pathname]);

  return (
    <nav className="w-full border-b border-border bg-raised">
      <div className="max-w-7xl mx-auto px-4 sm:px-6 lg:px-8">
        <div className="flex items-center justify-between h-16">
          <div className="flex-shrink-0">
            <Link
              href={isAuthenticated ? '/dashboard' : '/login'}
              className="flex items-center gap-2.5 text-ink hover:opacity-80 transition-opacity"
            >
              <span className="relative inline-flex h-8 w-8 shrink-0">
                {/* eslint-disable-next-line @next/next/no-img-element */}
                <img
                  src="/tempo-mark-ink.png"
                  alt=""
                  width={32}
                  height={32}
                  className="h-8 w-8 rounded-md object-contain dark:hidden"
                />
                {/* eslint-disable-next-line @next/next/no-img-element */}
                <img
                  src="/tempo-mark-volt.png"
                  alt=""
                  width={32}
                  height={32}
                  className="hidden h-8 w-8 rounded-md object-contain dark:block"
                />
              </span>
              <span className="text-2xl font-bold font-sans">Tempo</span>
            </Link>
          </div>

          <div className="flex items-center">
            {isAuthenticated && (
              <div className="hidden md:flex items-center space-x-1">
                <Link href="/dashboard" className={navLinkClasses('/dashboard')}>
                  Dashboard
                </Link>
                <Link href="/activities" className={navLinkClasses('/activities')}>
                  Activities
                </Link>
                <Link href="/import" className={navLinkClasses('/import')}>
                  Import
                </Link>
                <Link href="/settings" className={navLinkClasses('/settings')}>
                  Settings
                </Link>
              </div>
            )}

            {isAuthenticated && (
              <div className="hidden md:flex items-center ml-4">
                <button
                  onClick={logout}
                  className="px-3 py-1.5 text-sm font-medium text-muted hover:text-ink hover:bg-canvas rounded-tempo transition-colors"
                >
                  Logout
                </button>
              </div>
            )}

            <button
              type="button"
              className="md:hidden inline-flex flex-col items-center justify-center gap-1.5 p-2 rounded-tempo text-muted hover:text-ink hover:bg-canvas focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-volt focus:ring-offset-raised"
              aria-label="Toggle navigation menu"
              aria-expanded={mobileOpen}
              aria-controls="mobile-menu"
              onClick={() => setMobileOpen((open) => !open)}
            >
              <span className="sr-only">Open main menu</span>
              <span
                className={`block h-0.5 w-5 rounded-sm bg-current transition-transform ${
                  mobileOpen ? 'translate-y-1.5 rotate-45' : ''
                }`}
              />
              <span
                className={`block h-0.5 w-5 rounded-sm bg-current transition-opacity ${
                  mobileOpen ? 'opacity-0' : 'opacity-100'
                }`}
              />
              <span
                className={`block h-0.5 w-5 rounded-sm bg-current transition-transform ${
                  mobileOpen ? '-translate-y-1.5 -rotate-45' : ''
                }`}
              />
            </button>
          </div>
        </div>

        {isAuthenticated && (
          <div
            id="mobile-menu"
            className={`md:hidden pb-3 space-y-1 ${
              mobileOpen ? 'block' : 'hidden'
            }`}
          >
            <Link
              href="/dashboard"
              className={navLinkClasses('/dashboard')}
              onClick={() => setMobileOpen(false)}
            >
              Dashboard
            </Link>
            <Link
              href="/activities"
              className={navLinkClasses('/activities')}
              onClick={() => setMobileOpen(false)}
            >
              Activities
            </Link>
            <Link
              href="/import"
              className={navLinkClasses('/import')}
              onClick={() => setMobileOpen(false)}
            >
              Import
            </Link>
            <Link
              href="/settings"
              className={navLinkClasses('/settings')}
              onClick={() => setMobileOpen(false)}
            >
              Settings
            </Link>
            <button
              onClick={() => {
                logout();
                setMobileOpen(false);
              }}
              className="w-full text-left px-4 py-2 text-sm font-medium text-muted hover:text-ink hover:bg-canvas rounded-tempo transition-colors"
            >
              Logout
            </button>
          </div>
        )}
      </div>
    </nav>
  );
}

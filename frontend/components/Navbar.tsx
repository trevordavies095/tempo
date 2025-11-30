'use client';

import Link from 'next/link';
import { usePathname } from 'next/navigation';
import { useEffect, useState } from 'react';
import { useAuth } from '@/contexts/AuthContext';

export function Navbar() {
  const pathname = usePathname();
  const [mobileOpen, setMobileOpen] = useState(false);
  const { isAuthenticated, isLoading, logout, user } = useAuth();

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
      'block px-4 py-2 text-sm font-medium rounded-md transition-colors';
    const active =
      'bg-gray-100 dark:bg-gray-800 text-gray-900 dark:text-gray-100';
    const inactive =
      'text-gray-600 dark:text-gray-400 hover:text-gray-900 dark:hover:text-gray-100 hover:bg-gray-50 dark:hover:bg-gray-800';

    return `${base} ${isActive(path) ? active : inactive}`;
  };

  // Close mobile menu when route changes
  useEffect(() => {
    setMobileOpen(false);
  }, [pathname]);

  return (
    <nav className="w-full border-b border-gray-200 dark:border-gray-800 bg-white dark:bg-gray-900">
      <div className="max-w-7xl mx-auto px-4 sm:px-6 lg:px-8">
        <div className="flex items-center justify-between h-16">
          {/* Logo/Brand */}
          <div className="flex-shrink-0">
            <Link
              href={isAuthenticated ? "/dashboard" : "/login"}
              className="text-2xl font-bold text-gray-900 dark:text-gray-100 hover:text-gray-700 dark:hover:text-gray-300 transition-colors"
            >
              Tempo
            </Link>
          </div>

          <div className="flex items-center">
            {/* Desktop Navigation Links */}
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

            {/* User info and logout */}
            {isAuthenticated && (
              <div className="hidden md:flex items-center space-x-4 ml-4">
                {user && (
                  <span className="text-sm text-gray-600 dark:text-gray-400">
                    {user.username}
                  </span>
                )}
                <button
                  onClick={logout}
                  className="px-3 py-1.5 text-sm font-medium text-gray-600 dark:text-gray-400 hover:text-gray-900 dark:hover:text-gray-100 hover:bg-gray-50 dark:hover:bg-gray-800 rounded-md transition-colors"
                >
                  Logout
                </button>
              </div>
            )}

            {/* Mobile menu button */}
            <button
              type="button"
              className="md:hidden inline-flex flex-col items-center justify-center gap-1.5 p-2 rounded-md text-gray-600 dark:text-gray-400 hover:text-gray-900 dark:hover:text-gray-100 hover:bg-gray-50 dark:hover:bg-gray-800 focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-blue-500"
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

        {/* Mobile Navigation Panel */}
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
            {user && (
              <div className="px-4 py-2 text-sm text-gray-600 dark:text-gray-400">
                {user.username}
              </div>
            )}
            <button
              onClick={() => {
                logout();
                setMobileOpen(false);
              }}
              className="w-full text-left px-4 py-2 text-sm font-medium text-gray-600 dark:text-gray-400 hover:text-gray-900 dark:hover:text-gray-100 hover:bg-gray-50 dark:hover:bg-gray-800 rounded-md transition-colors"
            >
              Logout
            </button>
          </div>
        )}
      </div>
    </nav>
  );
}


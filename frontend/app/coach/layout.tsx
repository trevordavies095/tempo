'use client';

import { useEffect } from 'react';
import Link from 'next/link';
import { usePathname, useRouter } from 'next/navigation';
import { AuthGuard } from '@/components/AuthGuard';
import { useCoachFeature } from '@/hooks/useCoachFeature';

const TABS = [
  { href: '/coach', label: 'Week', id: 'week' as const },
  { href: '/coach/history', label: 'History', id: 'history' as const },
  { href: '/coach/settings', label: 'Settings', id: 'settings' as const },
];

function resolveActiveTab(pathname: string): 'week' | 'history' | 'settings' {
  if (pathname === '/coach/history' || pathname.startsWith('/coach/history/')) {
    return 'history';
  }
  if (pathname === '/coach/settings' || pathname.startsWith('/coach/settings/')) {
    return 'settings';
  }
  return 'week';
}

function CoachTabBar() {
  const pathname = usePathname();
  const active = resolveActiveTab(pathname);

  return (
    <nav className="mb-4 border-b border-border" aria-label="Coach">
      <div className="flex gap-1" role="tablist">
        {TABS.map((tab) => {
          const selected = active === tab.id;
          return (
            <Link
              key={tab.id}
              href={tab.href}
              role="tab"
              id={`coach-tab-${tab.id}`}
              aria-selected={selected}
              tabIndex={selected ? 0 : -1}
              className={`px-4 py-2 text-sm font-medium rounded-t-tempo border-b-2 transition-colors focus:outline-none focus:ring-2 focus:ring-volt focus:ring-offset-2 focus:ring-offset-canvas ${
                selected
                  ? 'border-ink text-ink bg-canvas dark:border-volt dark:text-volt dark:bg-transparent'
                  : 'border-transparent text-muted hover:text-ink hover:bg-canvas'
              }`}
            >
              {tab.label}
            </Link>
          );
        })}
      </div>
    </nav>
  );
}

function CoachGate({ children }: { children: React.ReactNode }) {
  const router = useRouter();
  const { showCoachNav, isLoading: settingsLoading, isFetched } = useCoachFeature();

  useEffect(() => {
    if (isFetched && !showCoachNav) {
      router.replace('/dashboard');
    }
  }, [isFetched, showCoachNav, router]);

  if (settingsLoading || !isFetched) {
    return (
      <div className="min-h-screen bg-canvas text-ink">
        <main className="mx-auto w-full max-w-4xl py-8 px-6">
          <p className="text-sm text-muted">Loading...</p>
        </main>
      </div>
    );
  }

  if (!showCoachNav) {
    return null;
  }

  return (
    <div className="min-h-screen bg-canvas text-ink">
      <div className="mx-auto w-full max-w-4xl px-6 pt-8">
        <CoachTabBar />
      </div>
      {children}
    </div>
  );
}

export default function CoachLayout({ children }: { children: React.ReactNode }) {
  return (
    <AuthGuard>
      <CoachGate>{children}</CoachGate>
    </AuthGuard>
  );
}

'use client';

import { useEffect, useMemo, type ReactNode } from 'react';
import { useSearchParams, useRouter, usePathname } from 'next/navigation';
import { Tabs, type TabItem } from '@/components/ui/Tabs';

export type TabId = 'overview' | 'comparison';

interface ActivityDetailTabsProps {
  overviewContent: ReactNode;
  comparisonContent: ReactNode;
  showComparisonTab?: boolean;
  isLoadingSimilarRoutes?: boolean;
}

function isValidTabId(value: string | null): value is TabId {
  return value === 'overview' || value === 'comparison';
}

export function ActivityDetailTabs({
  overviewContent,
  comparisonContent,
  showComparisonTab = true,
  isLoadingSimilarRoutes = false,
}: ActivityDetailTabsProps) {
  const searchParams = useSearchParams();
  const router = useRouter();
  const pathname = usePathname();

  const tabFromUrl = useMemo((): TabId => {
    const tabParam = searchParams.get('tab');
    if (isValidTabId(tabParam)) {
      return tabParam;
    }
    return 'overview';
  }, [searchParams]);

  const comparisonUnavailable =
    tabFromUrl === 'comparison' && !showComparisonTab && !isLoadingSimilarRoutes;
  const activeTab: TabId = comparisonUnavailable ? 'overview' : tabFromUrl;

  useEffect(() => {
    const tabParam = searchParams.get('tab');

    if (tabParam !== null && !isValidTabId(tabParam)) {
      router.replace(pathname);
      return;
    }

    if (comparisonUnavailable) {
      router.replace(pathname);
    }
  }, [searchParams, router, pathname, comparisonUnavailable]);

  const handleTabChange = (tab: TabId) => {
    if (tab === 'overview') {
      router.push(pathname);
    } else {
      router.push(`${pathname}?tab=${tab}`);
    }
  };

  const items: TabItem<TabId>[] = [
    { id: 'overview', label: 'Overview' },
    ...(showComparisonTab ? [{ id: 'comparison' as const, label: 'Route Comparison' }] : []),
  ];

  return (
    <Tabs
      items={items}
      value={activeTab}
      onChange={handleTabChange}
      aria-label="Workout overview tabs"
    >
      {activeTab === 'overview' && overviewContent}
      {activeTab === 'comparison' && comparisonContent}
    </Tabs>
  );
}

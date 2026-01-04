'use client';

import { useState, useEffect, useRef, type ReactNode } from 'react';
import { useSearchParams, useRouter, usePathname } from 'next/navigation';
import type { WorkoutDetail } from '@/lib/api';

export type TabId = 'overview' | 'comparison';

interface ActivityDetailTabsProps {
  workoutId: string;
  currentWorkout: WorkoutDetail;
  overviewContent: ReactNode;
  comparisonContent: ReactNode;
}

export function ActivityDetailTabs({
  workoutId,
  currentWorkout,
  overviewContent,
  comparisonContent,
}: ActivityDetailTabsProps) {
  const searchParams = useSearchParams();
  const router = useRouter();
  const pathname = usePathname();
  const tabRefs = useRef<{ [key in TabId]: HTMLButtonElement | null }>({
    overview: null,
    comparison: null,
  });

  // Read initial tab from URL
  const getTabFromUrl = (): TabId => {
    const tabParam = searchParams.get('tab');
    if (tabParam === 'comparison') {
      return 'comparison';
    }
    return 'overview';
  };

  const [activeTab, setActiveTab] = useState<TabId>(() => {
    // Initialize from URL on mount
    const tabParam = searchParams.get('tab');
    if (tabParam === 'comparison') {
      return 'comparison';
    }
    return 'overview';
  });

  // Sync with URL changes (browser back/forward)
  useEffect(() => {
    const tab = getTabFromUrl();
    setActiveTab(tab);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [searchParams]);

  // Handle tab change
  const handleTabChange = (tab: TabId) => {
    setActiveTab(tab);
    if (tab === 'overview') {
      // Remove param for overview
      router.push(pathname);
    } else {
      // Add param for comparison
      router.push(`${pathname}?tab=${tab}`);
    }
  };

  // Keyboard navigation
  const handleKeyDown = (e: React.KeyboardEvent<HTMLButtonElement>, currentTab: TabId) => {
    if (e.key === 'ArrowLeft' || e.key === 'ArrowRight') {
      e.preventDefault();
      const tabs: TabId[] = ['overview', 'comparison'];
      const currentIndex = tabs.indexOf(currentTab);
      const nextIndex =
        e.key === 'ArrowLeft'
          ? (currentIndex - 1 + tabs.length) % tabs.length
          : (currentIndex + 1) % tabs.length;
      const nextTab = tabs[nextIndex];
      handleTabChange(nextTab);
      // Focus the next tab button
      setTimeout(() => {
        tabRefs.current[nextTab]?.focus();
      }, 0);
    }
  };

  return (
    <div className="w-full">
      {/* Tab Navigation */}
      <nav
        className="mb-3 border-b border-gray-200 dark:border-gray-800"
        aria-label="Activity detail tabs"
      >
        <div className="flex space-x-1">
          <button
            ref={(el) => { tabRefs.current.overview = el; }}
            onClick={() => handleTabChange('overview')}
            onKeyDown={(e) => handleKeyDown(e, 'overview')}
            aria-selected={activeTab === 'overview'}
            role="tab"
            className={`px-4 py-2 text-sm font-medium transition-colors focus:outline-none focus:ring-2 focus:ring-blue-500 focus:ring-offset-2 dark:focus:ring-offset-gray-900 rounded-t-lg ${
              activeTab === 'overview'
                ? 'text-blue-600 dark:text-blue-400 border-b-2 border-blue-600 dark:border-blue-400 bg-blue-50 dark:bg-blue-900/20'
                : 'text-gray-600 dark:text-gray-400 hover:text-gray-900 dark:hover:text-gray-100 hover:bg-gray-50 dark:hover:bg-gray-800'
            }`}
          >
            Overview
          </button>
          <button
            ref={(el) => { tabRefs.current.comparison = el; }}
            onClick={() => handleTabChange('comparison')}
            onKeyDown={(e) => handleKeyDown(e, 'comparison')}
            aria-selected={activeTab === 'comparison'}
            role="tab"
            className={`px-4 py-2 text-sm font-medium transition-colors focus:outline-none focus:ring-2 focus:ring-blue-500 focus:ring-offset-2 dark:focus:ring-offset-gray-900 rounded-t-lg ${
              activeTab === 'comparison'
                ? 'text-blue-600 dark:text-blue-400 border-b-2 border-blue-600 dark:border-blue-400 bg-blue-50 dark:bg-blue-900/20'
                : 'text-gray-600 dark:text-gray-400 hover:text-gray-900 dark:hover:text-gray-100 hover:bg-gray-50 dark:hover:bg-gray-800'
            }`}
          >
            Route Comparison
          </button>
        </div>
      </nav>

      {/* Tab Content */}
      <div role="tabpanel" aria-labelledby={`tab-${activeTab}`}>
        {activeTab === 'overview' && overviewContent}
        {activeTab === 'comparison' && comparisonContent}
      </div>
    </div>
  );
}


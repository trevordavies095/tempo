'use client';

import { useQuery } from '@tanstack/react-query';
import { getWorkouts, type WorkoutsListParams } from '@/lib/api';
import { useSettings } from '@/lib/settings';
import Pagination from '@/components/Pagination';
import ActivitiesFilters from '@/components/ActivitiesFilters';
import ActivitiesTable from '@/components/ActivitiesTable';
import { useActivitiesFilters } from '@/hooks/useActivitiesFilters';
import { AuthGuard } from '@/components/AuthGuard';
import { PageShell } from '@/components/ui/PageShell';
import { Card } from '@/components/ui/Card';
import { EmptyState } from '@/components/ui/EmptyState';

function ActivitiesPageContent() {
  const { unitPreference } = useSettings();
  const {
    page,
    setPage,
    keyword,
    searchInput,
    setSearchInput,
    runType,
    sortBy,
    sortOrder,
    handleSearch,
    handleKeyPress,
    handleSort,
    handleRunTypeChange,
    getSortParam,
  } = useActivitiesFilters();

  const params: WorkoutsListParams = {
    page,
    pageSize: 20,
    keyword: keyword || undefined,
    runType: runType || undefined,
    sortBy: getSortParam(sortBy),
    sortOrder,
  };

  const { data, isLoading, isError, error } = useQuery({
    queryKey: ['workouts', 'activities', params],
    queryFn: () => getWorkouts(params),
  });


  return (
    <PageShell density="control" title="My Activities">
      <ActivitiesFilters
        searchInput={searchInput}
        onSearchInputChange={setSearchInput}
        onSearch={handleSearch}
        onKeyPress={handleKeyPress}
        runType={runType}
        onRunTypeChange={handleRunTypeChange}
      />

      <Card padding={false} className="overflow-hidden">
        {isLoading ? (
          <div className="p-6">
            <EmptyState title="Loading activities..." />
          </div>
        ) : isError ? (
          <div className="p-6">
            <EmptyState
              title="Could not load activities"
              description={error instanceof Error ? error.message : 'Please try again.'}
            />
          </div>
        ) : !data || data.items.length === 0 ? (
          <div className="p-6">
            <EmptyState title="No activities found" />
          </div>
        ) : (
          <>
            <div className="px-6 py-4 border-b border-border">
              <h2 className="text-lg font-semibold text-ink">
                {data.totalCount} {data.totalCount === 1 ? 'Activity' : 'Activities'}
              </h2>
            </div>
            <ActivitiesTable
              workouts={data.items}
              unitPreference={unitPreference}
              sortBy={sortBy}
              sortOrder={sortOrder}
              onSort={handleSort}
            />
            <Pagination
              currentPage={data.page}
              totalPages={data.totalPages}
              onPageChange={setPage}
              className="px-6 py-4 border-t border-border"
            />
          </>
        )}
      </Card>
    </PageShell>
  );
}

export default function ActivitiesPage() {
  return (
    <AuthGuard>
      <ActivitiesPageContent />
    </AuthGuard>
  );
}

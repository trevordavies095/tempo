'use client';

import { useQuery } from '@tanstack/react-query';
import { getWorkouts, type WorkoutsListParams } from '@/lib/api';
import { useSettings } from '@/lib/settings';
import Pagination from '@/components/Pagination';
import LoadingState from '@/components/LoadingState';
import ErrorState from '@/components/ErrorState';
import ActivitiesFilters from '@/components/ActivitiesFilters';
import ActivitiesTable from '@/components/ActivitiesTable';
import { useActivitiesFilters } from '@/hooks/useActivitiesFilters';
import { AuthGuard } from '@/components/AuthGuard';

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
    <div className="flex min-h-screen items-center justify-center bg-zinc-50 dark:bg-black">
      <main className="flex min-h-screen w-full max-w-7xl flex-col items-start py-8 px-4 sm:px-6 lg:px-8">
        <div className="w-full mb-8">
          <h1 className="text-4xl font-bold text-gray-900 dark:text-gray-100 mb-2">
            My Activities
          </h1>
        </div>

        <ActivitiesFilters
          searchInput={searchInput}
          onSearchInputChange={setSearchInput}
          onSearch={handleSearch}
          onKeyPress={handleKeyPress}
          runType={runType}
          onRunTypeChange={handleRunTypeChange}
        />

        <div className="w-full bg-white dark:bg-gray-900 rounded-lg border border-gray-200 dark:border-gray-800 overflow-hidden">
          {isLoading ? (
            <LoadingState message="Loading activities..." className="p-8 text-center" />
          ) : isError ? (
            <ErrorState error={error} message="Error loading activities. Please try again." className="p-8 text-center" />
          ) : !data || data.items.length === 0 ? (
            <div className="p-8 text-center text-gray-600 dark:text-gray-400">
              No activities found.
            </div>
          ) : (
            <>
              <div className="px-6 py-4 border-b border-gray-200 dark:border-gray-800">
                <h2 className="text-lg font-semibold text-gray-900 dark:text-gray-100">
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
                className="px-6 py-4 border-t border-gray-200 dark:border-gray-800"
              />
            </>
          )}
        </div>
      </main>
    </div>
  );
}

export default function ActivitiesPage() {
  return (
    <AuthGuard>
      <ActivitiesPageContent />
    </AuthGuard>
  );
}


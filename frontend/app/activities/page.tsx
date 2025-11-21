'use client';

import { useQuery } from '@tanstack/react-query';
import Link from 'next/link';
import { useState, useCallback } from 'react';
import { getWorkouts, type WorkoutsListParams } from '@/lib/api';
import { formatDuration, formatDistance, formatElevation, getWorkoutDisplayName } from '@/lib/format';
import { useSettings } from '@/lib/settings';

type SortColumn = 'startedAt' | 'name' | 'durationS' | 'distanceM' | 'elevGainM';
type SortOrder = 'asc' | 'desc';

const RUN_TYPES = [
  { value: '', label: 'All Run Types' },
  { value: 'Race', label: 'Race' },
  { value: 'Workout', label: 'Workout' },
  { value: 'Long Run', label: 'Long Run' },
  { value: 'Easy Run', label: 'Easy Run' },
];

const SORT_COLUMN_MAP: Record<string, SortColumn> = {
  startedAt: 'startedAt',
  name: 'name',
  durationS: 'durationS',
  distanceM: 'distanceM',
  elevGainM: 'elevGainM',
};

export default function ActivitiesPage() {
  const { unitPreference } = useSettings();
  const [page, setPage] = useState(1);
  const [keyword, setKeyword] = useState('');
  const [searchInput, setSearchInput] = useState('');
  const [runType, setRunType] = useState('');
  const [sortBy, setSortBy] = useState<SortColumn>('startedAt');
  const [sortOrder, setSortOrder] = useState<SortOrder>('desc');

  const handleSearch = useCallback(() => {
    setKeyword(searchInput);
    setPage(1);
  }, [searchInput]);

  const handleKeyPress = useCallback((e: React.KeyboardEvent<HTMLInputElement>) => {
    if (e.key === 'Enter') {
      handleSearch();
    }
  }, [handleSearch]);

  const handleSort = useCallback((column: SortColumn) => {
    if (sortBy === column) {
      // Toggle sort order if clicking the same column
      setSortOrder(sortOrder === 'asc' ? 'desc' : 'asc');
    } else {
      // Set new column and default to descending
      setSortBy(column);
      setSortOrder('desc');
    }
    setPage(1);
  }, [sortBy, sortOrder]);

  const getSortParam = useCallback((column: SortColumn): string => {
    const apiColumnMap: Record<SortColumn, string> = {
      startedAt: 'startedAt',
      name: 'name',
      durationS: 'duration',
      distanceM: 'distance',
      elevGainM: 'elevation',
    };
    return apiColumnMap[column];
  }, []);

  const params: WorkoutsListParams = {
    page,
    pageSize: 20,
    keyword: keyword || undefined,
    runType: runType || undefined,
    sortBy: getSortParam(sortBy),
    sortOrder,
  };

  const { data, isLoading, isError } = useQuery({
    queryKey: ['workouts', 'activities', params],
    queryFn: () => getWorkouts(params),
  });

  const formatActivityDate = (dateString: string): string => {
    const date = new Date(dateString);
    const days = ['Sun', 'Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat'];
    const dayName = days[date.getDay()];
    const month = date.getMonth() + 1;
    const day = date.getDate();
    const year = date.getFullYear();
    return `${dayName}, ${month}/${day}/${year}`;
  };

  const getSortIcon = (column: SortColumn) => {
    if (sortBy !== column) {
      return (
        <span className="text-gray-400 dark:text-gray-500 ml-1">↓</span>
      );
    }
    return sortOrder === 'desc' ? (
      <span className="text-gray-900 dark:text-gray-100 ml-1">↓</span>
    ) : (
      <span className="text-gray-900 dark:text-gray-100 ml-1">↑</span>
    );
  };

  return (
    <div className="flex min-h-screen items-center justify-center bg-zinc-50 dark:bg-black">
      <main className="flex min-h-screen w-full max-w-7xl flex-col items-start py-8 px-4 sm:px-6 lg:px-8">
        <div className="w-full mb-8">
          <h1 className="text-4xl font-bold text-gray-900 dark:text-gray-100 mb-2">
            My Activities
          </h1>
        </div>

        {/* Filter Panel */}
        <div className="w-full bg-white dark:bg-gray-900 rounded-lg border border-gray-200 dark:border-gray-800 p-6 mb-6">
          <div className="flex flex-col sm:flex-row gap-4">
            {/* Keywords Search */}
            <div className="flex-1">
              <label htmlFor="keywords" className="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-2">
                Keywords
              </label>
              <div className="flex gap-2">
                <input
                  id="keywords"
                  type="text"
                  value={searchInput}
                  onChange={(e) => setSearchInput(e.target.value)}
                  onKeyPress={handleKeyPress}
                  placeholder="My Morning Workout"
                  className="flex-1 px-4 py-2 border border-gray-300 dark:border-gray-700 rounded-md bg-white dark:bg-gray-800 text-gray-900 dark:text-gray-100 focus:outline-none focus:ring-2 focus:ring-blue-500 focus:border-transparent"
                />
                <button
                  onClick={handleSearch}
                  className="px-4 py-2 bg-blue-600 text-white rounded-md hover:bg-blue-700 focus:outline-none focus:ring-2 focus:ring-blue-500 focus:ring-offset-2 transition-colors"
                >
                  Search
                </button>
              </div>
            </div>

            {/* Run Type Filter */}
            <div className="sm:w-48">
              <label htmlFor="runType" className="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-2">
                Run Type
              </label>
              <select
                id="runType"
                value={runType}
                onChange={(e) => {
                  setRunType(e.target.value);
                  setPage(1);
                }}
                className="w-full px-4 py-2 border border-gray-300 dark:border-gray-700 rounded-md bg-white dark:bg-gray-800 text-gray-900 dark:text-gray-100 focus:outline-none focus:ring-2 focus:ring-blue-500 focus:border-transparent"
              >
                {RUN_TYPES.map((type) => (
                  <option key={type.value} value={type.value}>
                    {type.label}
                  </option>
                ))}
              </select>
            </div>
          </div>
        </div>

        {/* Activities Table */}
        <div className="w-full bg-white dark:bg-gray-900 rounded-lg border border-gray-200 dark:border-gray-800 overflow-hidden">
          {isLoading ? (
            <div className="p-8 text-center text-gray-600 dark:text-gray-400">
              Loading activities...
            </div>
          ) : isError ? (
            <div className="p-8 text-center text-red-600 dark:text-red-400">
              Error loading activities. Please try again.
            </div>
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
              <div className="overflow-x-auto">
                <table className="w-full">
                  <thead className="bg-gray-50 dark:bg-gray-800">
                    <tr>
                      <th
                        className="px-6 py-3 text-left text-xs font-medium text-gray-500 dark:text-gray-400 uppercase tracking-wider cursor-pointer hover:bg-gray-100 dark:hover:bg-gray-700"
                        onClick={() => handleSort('startedAt')}
                      >
                        <div className="flex items-center">
                          Date
                          {getSortIcon('startedAt')}
                        </div>
                      </th>
                      <th
                        className="px-6 py-3 text-left text-xs font-medium text-gray-500 dark:text-gray-400 uppercase tracking-wider cursor-pointer hover:bg-gray-100 dark:hover:bg-gray-700"
                        onClick={() => handleSort('name')}
                      >
                        <div className="flex items-center">
                          Title
                          {getSortIcon('name')}
                        </div>
                      </th>
                      <th
                        className="px-6 py-3 text-left text-xs font-medium text-gray-500 dark:text-gray-400 uppercase tracking-wider cursor-pointer hover:bg-gray-100 dark:hover:bg-gray-700"
                        onClick={() => handleSort('durationS')}
                      >
                        <div className="flex items-center">
                          Time
                          {getSortIcon('durationS')}
                        </div>
                      </th>
                      <th
                        className="px-6 py-3 text-left text-xs font-medium text-gray-500 dark:text-gray-400 uppercase tracking-wider cursor-pointer hover:bg-gray-100 dark:hover:bg-gray-700"
                        onClick={() => handleSort('distanceM')}
                      >
                        <div className="flex items-center">
                          Distance
                          {getSortIcon('distanceM')}
                        </div>
                      </th>
                      <th
                        className="px-6 py-3 text-left text-xs font-medium text-gray-500 dark:text-gray-400 uppercase tracking-wider cursor-pointer hover:bg-gray-100 dark:hover:bg-gray-700"
                        onClick={() => handleSort('elevGainM')}
                      >
                        <div className="flex items-center">
                          Elevation
                          {getSortIcon('elevGainM')}
                        </div>
                      </th>
                    </tr>
                  </thead>
                  <tbody className="bg-white dark:bg-gray-900 divide-y divide-gray-200 dark:divide-gray-800">
                    {data.items.map((workout) => (
                      <tr key={workout.id} className="hover:bg-gray-50 dark:hover:bg-gray-800">
                        <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-900 dark:text-gray-100">
                          {formatActivityDate(workout.startedAt)}
                        </td>
                        <td className="px-6 py-4 text-sm">
                          <Link
                            href={`/dashboard/${workout.id}`}
                            className="text-blue-600 dark:text-blue-400 hover:text-blue-800 dark:hover:text-blue-300 hover:underline"
                          >
                            {getWorkoutDisplayName(workout.name, workout.startedAt)}
                          </Link>
                        </td>
                        <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-900 dark:text-gray-100">
                          {formatDuration(workout.durationS)}
                        </td>
                        <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-900 dark:text-gray-100">
                          {formatDistance(workout.distanceM, unitPreference)}
                        </td>
                        <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-900 dark:text-gray-100">
                          {workout.elevGainM !== null && workout.elevGainM !== undefined
                            ? formatElevation(workout.elevGainM, unitPreference)
                            : '—'}
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
              {/* Pagination */}
              {data.totalPages > 1 && (
                <div className="px-6 py-4 border-t border-gray-200 dark:border-gray-800 flex items-center justify-between">
                  <div className="text-sm text-gray-600 dark:text-gray-400">
                    Page {data.page} of {data.totalPages}
                  </div>
                  <div className="flex gap-2">
                    <button
                      onClick={() => setPage((p) => Math.max(1, p - 1))}
                      disabled={page === 1}
                      className="px-4 py-2 text-sm font-medium text-gray-700 dark:text-gray-300 bg-white dark:bg-gray-800 border border-gray-300 dark:border-gray-700 rounded-lg hover:bg-gray-50 dark:hover:bg-gray-700 disabled:opacity-50 disabled:cursor-not-allowed transition-colors"
                    >
                      Previous
                    </button>
                    <button
                      onClick={() => setPage((p) => Math.min(data.totalPages, p + 1))}
                      disabled={page === data.totalPages}
                      className="px-4 py-2 text-sm font-medium text-gray-700 dark:text-gray-300 bg-white dark:bg-gray-800 border border-gray-300 dark:border-gray-700 rounded-lg hover:bg-gray-50 dark:hover:bg-gray-700 disabled:opacity-50 disabled:cursor-not-allowed transition-colors"
                    >
                      Next
                    </button>
                  </div>
                </div>
              )}
            </>
          )}
        </div>
      </main>
    </div>
  );
}


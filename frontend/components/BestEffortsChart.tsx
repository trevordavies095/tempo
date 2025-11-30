'use client';

import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';
import { getBestEfforts, recalculateBestEfforts, type BestEffortItem } from '@/lib/api';
import Link from 'next/link';

/// <summary>
/// Format seconds as MM:SS or HH:MM:SS
/// </summary>
function formatTime(seconds: number): string {
  const hours = Math.floor(seconds / 3600);
  const minutes = Math.floor((seconds % 3600) / 60);
  const secs = seconds % 60;

  if (hours > 0) {
    return `${hours}:${minutes.toString().padStart(2, '0')}:${secs.toString().padStart(2, '0')}`;
  }
  return `${minutes}:${secs.toString().padStart(2, '0')}`;
}


export default function BestEffortsChart() {
  const [showRecalculateConfirm, setShowRecalculateConfirm] = useState(false);
  const queryClient = useQueryClient();

  const { data, isLoading, isError, error } = useQuery({
    queryKey: ['bestEfforts'],
    queryFn: () => getBestEfforts(),
  });

  const recalculateMutation = useMutation({
    mutationFn: recalculateBestEfforts,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['bestEfforts'] });
      setShowRecalculateConfirm(false);
    },
  });

  const handleRecalculate = () => {
    setShowRecalculateConfirm(true);
  };

  const handleConfirmRecalculate = () => {
    recalculateMutation.mutate();
  };

  const handleCancelRecalculate = () => {
    setShowRecalculateConfirm(false);
  };

  if (isLoading) {
    return (
      <div className="bg-white dark:bg-gray-800 rounded-lg border border-gray-200 dark:border-gray-700 p-6">
        <div className="flex items-center justify-between mb-4">
          <h2 className="text-lg font-semibold text-gray-900 dark:text-gray-100">
            Best Efforts
          </h2>
        </div>
        <div className="h-64 flex items-center justify-center">
          <p className="text-sm text-gray-600 dark:text-gray-400">Loading...</p>
        </div>
      </div>
    );
  }

  if (isError) {
    return (
      <div className="bg-white dark:bg-gray-800 rounded-lg border border-gray-200 dark:border-gray-700 p-6">
        <div className="flex items-center justify-between mb-4">
          <h2 className="text-lg font-semibold text-gray-900 dark:text-gray-100">
            Best Efforts
          </h2>
        </div>
        <div className="h-64 flex items-center justify-center">
          <p className="text-sm text-red-600 dark:text-red-400">
            Error: {error instanceof Error ? error.message : 'Failed to load best efforts'}
          </p>
        </div>
      </div>
    );
  }

  if (!data || !data.distances || data.distances.length === 0) {
    return (
      <div className="bg-white dark:bg-gray-800 rounded-lg border border-gray-200 dark:border-gray-700 p-6">
        <div className="flex items-center gap-2 mb-4">
          <h2 className="text-lg font-semibold text-gray-900 dark:text-gray-100">
            Best Efforts
          </h2>
          <button
            onClick={handleRecalculate}
            className="p-1.5 text-gray-500 dark:text-gray-400 hover:text-gray-700 dark:hover:text-gray-200 transition-colors"
            title="Recalculate best efforts"
          >
            <svg
              className="w-4 h-4"
              fill="none"
              stroke="currentColor"
              viewBox="0 0 24 24"
              xmlns="http://www.w3.org/2000/svg"
            >
              <path
                strokeLinecap="round"
                strokeLinejoin="round"
                strokeWidth={2}
                d="M4 4v5h.582m15.356 2A8.001 8.001 0 004.582 9m0 0H9m11 11v-5h-.581m0 0a8.003 8.003 0 01-15.357-2m15.357 2H15"
              />
            </svg>
          </button>
        </div>
        <div className="h-64 flex items-center justify-center">
          <p className="text-sm text-gray-600 dark:text-gray-400">
            No best efforts data available. Click "Recalculate" to calculate best efforts from your workouts.
          </p>
        </div>
        {showRecalculateConfirm && (
          <div className="fixed inset-0 bg-white/80 dark:bg-gray-900/80 backdrop-blur-sm flex items-center justify-center z-50">
            <div className="bg-white dark:bg-gray-800 rounded-lg p-6 max-w-md mx-4 border border-gray-200 dark:border-gray-700 shadow-xl">
              <h3 className="text-lg font-semibold text-gray-900 dark:text-gray-100 mb-2">
                Recalculate Best Efforts
              </h3>
              <p className="text-sm text-gray-600 dark:text-gray-400 mb-4">
                Recalculating best efforts may take a few moments. Continue?
              </p>
              <div className="flex gap-3 justify-end">
                <button
                  onClick={handleCancelRecalculate}
                  className="px-4 py-2 text-sm font-medium text-gray-700 dark:text-gray-300 bg-gray-100 dark:bg-gray-700 rounded-md hover:bg-gray-200 dark:hover:bg-gray-600 transition-colors"
                >
                  Cancel
                </button>
                <button
                  onClick={handleConfirmRecalculate}
                  disabled={recalculateMutation.isPending}
                  className="px-4 py-2 text-sm font-medium text-white bg-blue-600 rounded-md hover:bg-blue-700 disabled:opacity-50 disabled:cursor-not-allowed transition-colors"
                >
                  {recalculateMutation.isPending ? 'Recalculating...' : 'Recalculate'}
                </button>
              </div>
            </div>
          </div>
        )}
      </div>
    );
  }

  // Sort by distance in meters (ascending)
  const sortedData = [...data.distances].sort((a, b) => a.distanceM - b.distanceM);

  return (
    <div className="bg-white dark:bg-gray-800 rounded-lg border border-gray-200 dark:border-gray-700 p-6">
      <div className="flex items-center gap-2 mb-4">
        <h2 className="text-lg font-semibold text-gray-900 dark:text-gray-100">
          Best Efforts
        </h2>
        <button
          onClick={handleRecalculate}
          disabled={recalculateMutation.isPending}
          className="p-1.5 text-gray-500 dark:text-gray-400 hover:text-gray-700 dark:hover:text-gray-200 disabled:opacity-50 disabled:cursor-not-allowed transition-colors"
          title="Recalculate best efforts"
        >
          <svg
            className={`w-4 h-4 ${recalculateMutation.isPending ? 'animate-spin' : ''}`}
            fill="none"
            stroke="currentColor"
            viewBox="0 0 24 24"
            xmlns="http://www.w3.org/2000/svg"
          >
            <path
              strokeLinecap="round"
              strokeLinejoin="round"
              strokeWidth={2}
              d="M4 4v5h.582m15.356 2A8.001 8.001 0 004.582 9m0 0H9m11 11v-5h-.581m0 0a8.003 8.003 0 01-15.357-2m15.357 2H15"
            />
          </svg>
        </button>
      </div>

      <div className="overflow-x-auto">
        <table className="w-full">
          <tbody className="bg-white dark:bg-gray-800 divide-y divide-gray-200 dark:divide-gray-700">
            {sortedData.map((item) => (
              <tr key={item.distance} className="bg-white dark:bg-gray-800">
                <td className="px-3 py-2 whitespace-nowrap text-sm font-medium text-gray-900 dark:text-gray-100">
                  {item.distance}
                </td>
                <td className="px-3 py-2 whitespace-nowrap text-sm">
                  <Link
                    href={`/dashboard/${item.workoutId}`}
                    className="text-blue-600 dark:text-blue-400 hover:text-blue-800 dark:hover:text-blue-300 hover:underline"
                  >
                    {formatTime(item.timeS)}
                  </Link>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>

      {showRecalculateConfirm && (
        <div className="fixed inset-0 bg-white/80 dark:bg-gray-900/80 backdrop-blur-sm flex items-center justify-center z-50">
          <div className="bg-white dark:bg-gray-800 rounded-lg p-6 max-w-md mx-4 border border-gray-200 dark:border-gray-700 shadow-xl">
            <h3 className="text-lg font-semibold text-gray-900 dark:text-gray-100 mb-2">
              Recalculate Best Efforts
            </h3>
            <p className="text-sm text-gray-600 dark:text-gray-400 mb-4">
              Recalculating best efforts may take a few moments. Continue?
            </p>
            <div className="flex gap-3 justify-end">
              <button
                onClick={handleCancelRecalculate}
                disabled={recalculateMutation.isPending}
                className="px-4 py-2 text-sm font-medium text-gray-700 dark:text-gray-300 bg-gray-100 dark:bg-gray-700 rounded-md hover:bg-gray-200 dark:hover:bg-gray-600 disabled:opacity-50 disabled:cursor-not-allowed transition-colors"
              >
                Cancel
              </button>
              <button
                onClick={handleConfirmRecalculate}
                disabled={recalculateMutation.isPending}
                className="px-4 py-2 text-sm font-medium text-white bg-blue-600 rounded-md hover:bg-blue-700 disabled:opacity-50 disabled:cursor-not-allowed transition-colors"
              >
                {recalculateMutation.isPending ? 'Recalculating...' : 'Recalculate'}
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}


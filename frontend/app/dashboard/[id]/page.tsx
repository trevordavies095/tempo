'use client';

import { useState, useEffect } from 'react';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import Link from 'next/link';
import { useParams } from 'next/navigation';
import dynamic from 'next/dynamic';
import { getWorkout, getWorkoutMedia, updateWorkout, type WorkoutMedia } from '@/lib/api';
import { formatDate, formatDateTime, formatDistance, formatDuration, formatPace, formatElevation, getWorkoutDisplayName } from '@/lib/format';
import { useSettings } from '@/lib/settings';
import { WorkoutMediaGallery } from '@/components/WorkoutMediaGallery';
import { MediaModal } from '@/components/MediaModal';
import { WeatherDisplay } from '@/components/WeatherDisplay';

// Dynamically import WorkoutMap to avoid SSR issues with Leaflet
const WorkoutMap = dynamic(() => import('@/components/WorkoutMap'), {
  ssr: false,
  loading: () => (
    <div className="flex items-center justify-center h-96 bg-gray-100 dark:bg-gray-800 rounded-lg border border-gray-200 dark:border-gray-700">
      <p className="text-gray-500 dark:text-gray-400">Loading map...</p>
    </div>
  ),
});

export default function WorkoutDetailPage() {
  const params = useParams();
  const id = params.id as string;
  const [selectedMediaIndex, setSelectedMediaIndex] = useState<number | null>(null);
  const [isModalOpen, setIsModalOpen] = useState(false);
  const [isEditingRunType, setIsEditingRunType] = useState(false);
  const [isEditingNotes, setIsEditingNotes] = useState(false);
  const [notesValue, setNotesValue] = useState<string>('');
  const { unitPreference } = useSettings();
  const queryClient = useQueryClient();

  const { data, isLoading, isError, error } = useQuery({
    queryKey: ['workout', id],
    queryFn: () => getWorkout(id),
  });

  const { data: media, isLoading: isLoadingMedia, isError: isMediaError } = useQuery({
    queryKey: ['workout-media', id],
    queryFn: () => getWorkoutMedia(id),
    enabled: !!id, // Fetch media as soon as we have the workout ID
    retry: false, // Don't retry on error - treat as no media
  });

  const updateWorkoutMutation = useMutation({
    mutationFn: (updates: { runType?: string | null; notes?: string | null }) => updateWorkout(id, updates),
    onSuccess: () => {
      // Invalidate and refetch workout data
      queryClient.invalidateQueries({ queryKey: ['workout', id] });
      setIsEditingRunType(false);
      setIsEditingNotes(false);
    },
  });

  // Sync notesValue with data.notes when entering edit mode or data changes
  useEffect(() => {
    if (data && isEditingNotes) {
      setNotesValue(data.notes || '');
    }
  }, [data, isEditingNotes]);

  // Debug logging for media query
  useEffect(() => {
    console.log('[WorkoutDetailPage] Media query state:', {
      isLoading: isLoadingMedia,
      isError: isMediaError,
      media: media,
      mediaType: typeof media,
      isArray: Array.isArray(media),
      length: Array.isArray(media) ? media.length : 'N/A',
      workoutId: id,
    });
  }, [media, isLoadingMedia, isMediaError, id]);

  const handleMediaClick = (media: WorkoutMedia, index: number) => {
    setSelectedMediaIndex(index);
    setIsModalOpen(true);
  };

  const handleCloseModal = () => {
    setIsModalOpen(false);
    setSelectedMediaIndex(null);
  };

  const handleSaveNotes = () => {
    const trimmedNotes = notesValue.trim() || null;
    updateWorkoutMutation.mutate({ notes: trimmedNotes });
  };

  const handleCancelNotes = () => {
    setIsEditingNotes(false);
    setNotesValue(data?.notes || '');
  };

  if (isLoading) {
    return (
      <div className="flex min-h-screen items-center justify-center bg-zinc-50 dark:bg-black">
        <main className="flex min-h-screen w-full max-w-6xl flex-col items-start py-16 px-8">
          <div className="w-full">
            <h1 className="text-4xl font-bold text-gray-900 dark:text-gray-100 mb-2">
              Workout Details
            </h1>
            <p className="text-lg text-gray-600 dark:text-gray-400 mb-8">
              Loading workout...
            </p>
          </div>
        </main>
      </div>
    );
  }

  if (isError) {
    const isNotFound = error instanceof Error && error.message === 'Workout not found';
    return (
      <div className="flex min-h-screen items-center justify-center bg-zinc-50 dark:bg-black">
        <main className="flex min-h-screen w-full max-w-6xl flex-col items-start py-16 px-8">
          <div className="w-full">
            <Link
              href="/dashboard"
              className="text-blue-600 dark:text-blue-400 hover:underline mb-4 inline-block"
            >
              ← Back to Dashboard
            </Link>
            <h1 className="text-4xl font-bold text-gray-900 dark:text-gray-100 mb-2">
              Workout Details
            </h1>
            <div className="p-4 bg-red-50 dark:bg-red-900/20 border border-red-200 dark:border-red-800 rounded-lg">
              <p className="text-sm text-red-800 dark:text-red-200">
                {isNotFound ? 'Workout not found' : `Error: ${error instanceof Error ? error.message : 'Failed to load workout'}`}
              </p>
            </div>
          </div>
        </main>
      </div>
    );
  }

  if (!data) {
    return null;
  }

  return (
    <div className="flex min-h-screen items-start justify-center bg-zinc-50 dark:bg-black">
      <main className="flex min-h-screen w-full max-w-6xl flex-col items-start py-16 px-8">
        <div className="w-full mb-8">
          <div className="flex items-center justify-between mb-4">
            <Link
              href="/dashboard"
              className="text-blue-600 dark:text-blue-400 hover:underline"
            >
              ← Back to Dashboard
            </Link>
            <Link
              href="/settings"
              className="px-4 py-2 text-sm font-medium text-gray-700 dark:text-gray-300 hover:text-gray-900 dark:hover:text-gray-100"
            >
              Settings
            </Link>
          </div>
          <h1 className="text-4xl font-bold text-gray-900 dark:text-gray-100 mb-2">
            {getWorkoutDisplayName(data.name, data.startedAt)}
          </h1>
          <p className="text-lg text-gray-600 dark:text-gray-400">
            {formatDateTime(data.startedAt)}
          </p>
        </div>

        <div className="w-full space-y-8">
          {/* Main Stats */}
          <div className="grid grid-cols-2 md:grid-cols-4 gap-4">
            <div className="bg-white dark:bg-gray-900 p-6 rounded-lg border border-gray-200 dark:border-gray-800">
              <div className="text-sm text-gray-600 dark:text-gray-400 mb-1">Distance</div>
              <div className="text-2xl font-bold text-gray-900 dark:text-gray-100">
                {formatDistance(data.distanceM, unitPreference)}
              </div>
            </div>
            <div className="bg-white dark:bg-gray-900 p-6 rounded-lg border border-gray-200 dark:border-gray-800">
              <div className="text-sm text-gray-600 dark:text-gray-400 mb-1">Duration</div>
              <div className="text-2xl font-bold text-gray-900 dark:text-gray-100">
                {formatDuration(data.durationS)}
              </div>
              {data.movingTimeS !== null && data.movingTimeS !== data.durationS && (
                <div className="text-xs text-gray-500 dark:text-gray-400 mt-1">
                  Moving: {formatDuration(data.movingTimeS)}
                </div>
              )}
            </div>
            <div className="bg-white dark:bg-gray-900 p-6 rounded-lg border border-gray-200 dark:border-gray-800">
              <div className="text-sm text-gray-600 dark:text-gray-400 mb-1">Pace</div>
              <div className="text-2xl font-bold text-gray-900 dark:text-gray-100">
                {formatPace(data.avgPaceS, unitPreference)}
              </div>
            </div>
            <div className="bg-white dark:bg-gray-900 p-6 rounded-lg border border-gray-200 dark:border-gray-800">
              <div className="text-sm text-gray-600 dark:text-gray-400 mb-1">Elevation</div>
              <div className="text-2xl font-bold text-gray-900 dark:text-gray-100">
                {data.elevGainM !== null
                  ? formatElevation(data.elevGainM, unitPreference)
                  : '—'}
              </div>
              {data.elevLossM !== null && (
                <div className="text-xs text-gray-500 dark:text-gray-400 mt-1">
                  Loss: {formatElevation(data.elevLossM, unitPreference)}
                </div>
              )}
            </div>
          </div>

          {/* Additional Stats */}
          {(data.maxSpeedMps !== null || data.avgSpeedMps !== null || 
            data.maxHeartRateBpm !== null || data.avgHeartRateBpm !== null ||
            data.maxCadenceRpm !== null || data.avgCadenceRpm !== null ||
            data.maxPowerWatts !== null || data.avgPowerWatts !== null ||
            data.calories !== null) && (
            <div className="grid grid-cols-2 md:grid-cols-4 gap-4">
              {data.maxSpeedMps !== null && (
                <div className="bg-white dark:bg-gray-900 p-6 rounded-lg border border-gray-200 dark:border-gray-800">
                  <div className="text-sm text-gray-600 dark:text-gray-400 mb-1">Max Speed</div>
                  <div className="text-2xl font-bold text-gray-900 dark:text-gray-100">
                    {(data.maxSpeedMps * 3.6).toFixed(1)} km/h
                  </div>
                </div>
              )}
              {data.maxHeartRateBpm !== null && (
                <div className="bg-white dark:bg-gray-900 p-6 rounded-lg border border-gray-200 dark:border-gray-800">
                  <div className="text-sm text-gray-600 dark:text-gray-400 mb-1">Max Heart Rate</div>
                  <div className="text-2xl font-bold text-gray-900 dark:text-gray-100">
                    {data.maxHeartRateBpm} bpm
                  </div>
                  {data.avgHeartRateBpm !== null && (
                    <div className="text-xs text-gray-500 dark:text-gray-400 mt-1">
                      Avg: {data.avgHeartRateBpm} bpm
                    </div>
                  )}
                </div>
              )}
              {data.maxCadenceRpm !== null && (
                <div className="bg-white dark:bg-gray-900 p-6 rounded-lg border border-gray-200 dark:border-gray-800">
                  <div className="text-sm text-gray-600 dark:text-gray-400 mb-1">Max Cadence</div>
                  <div className="text-2xl font-bold text-gray-900 dark:text-gray-100">
                    {data.maxCadenceRpm} rpm
                  </div>
                  {data.avgCadenceRpm !== null && (
                    <div className="text-xs text-gray-500 dark:text-gray-400 mt-1">
                      Avg: {data.avgCadenceRpm} rpm
                    </div>
                  )}
                </div>
              )}
              {data.maxPowerWatts !== null && (
                <div className="bg-white dark:bg-gray-900 p-6 rounded-lg border border-gray-200 dark:border-gray-800">
                  <div className="text-sm text-gray-600 dark:text-gray-400 mb-1">Max Power</div>
                  <div className="text-2xl font-bold text-gray-900 dark:text-gray-100">
                    {data.maxPowerWatts} W
                  </div>
                  {data.avgPowerWatts !== null && (
                    <div className="text-xs text-gray-500 dark:text-gray-400 mt-1">
                      Avg: {data.avgPowerWatts} W
                    </div>
                  )}
                </div>
              )}
              {data.calories !== null && (
                <div className="bg-white dark:bg-gray-900 p-6 rounded-lg border border-gray-200 dark:border-gray-800">
                  <div className="text-sm text-gray-600 dark:text-gray-400 mb-1">Calories</div>
                  <div className="text-2xl font-bold text-gray-900 dark:text-gray-100">
                    {data.calories}
                  </div>
                </div>
              )}
            </div>
          )}

          {/* Elevation Range */}
          {(data.minElevM !== null || data.maxElevM !== null) && (
            <div className="bg-white dark:bg-gray-900 p-6 rounded-lg border border-gray-200 dark:border-gray-800">
              <h2 className="text-xl font-semibold text-gray-900 dark:text-gray-100 mb-4">
                Elevation Profile
              </h2>
              <dl className="grid grid-cols-1 md:grid-cols-2 gap-4">
                {data.minElevM !== null && (
                  <div>
                    <dt className="text-sm font-medium text-gray-600 dark:text-gray-400">Min Elevation</dt>
                    <dd className="mt-1 text-sm text-gray-900 dark:text-gray-100">
                      {formatElevation(data.minElevM, unitPreference)}
                    </dd>
                  </div>
                )}
                {data.maxElevM !== null && (
                  <div>
                    <dt className="text-sm font-medium text-gray-600 dark:text-gray-400">Max Elevation</dt>
                    <dd className="mt-1 text-sm text-gray-900 dark:text-gray-100">
                      {formatElevation(data.maxElevM, unitPreference)}
                    </dd>
                  </div>
                )}
              </dl>
            </div>
          )}

          {/* Additional Info */}
          <div className="bg-white dark:bg-gray-900 p-6 rounded-lg border border-gray-200 dark:border-gray-800">
            <h2 className="text-xl font-semibold text-gray-900 dark:text-gray-100 mb-4">
              Workout Information
            </h2>
            <dl className="grid grid-cols-1 md:grid-cols-2 gap-4">
              <div>
                <dt className="text-sm font-medium text-gray-600 dark:text-gray-400">Run Type</dt>
                <dd className="mt-1 text-sm text-gray-900 dark:text-gray-100">
                  {isEditingRunType ? (
                    <div className="flex items-center gap-2">
                      <select
                        value={data.runType || ''}
                        onChange={(e) => {
                          const newValue = e.target.value === '' ? null : e.target.value;
                          updateWorkoutMutation.mutate({ runType: newValue });
                        }}
                        disabled={updateWorkoutMutation.isPending}
                        className="px-2 py-1 border border-gray-300 dark:border-gray-600 rounded bg-white dark:bg-gray-800 text-gray-900 dark:text-gray-100 focus:outline-none focus:ring-2 focus:ring-blue-500 disabled:opacity-50 disabled:cursor-not-allowed"
                        autoFocus
                      >
                        <option value="">None</option>
                        <option value="Race">Race</option>
                        <option value="Workout">Workout</option>
                        <option value="Long Run">Long Run</option>
                      </select>
                      <button
                        onClick={() => setIsEditingRunType(false)}
                        className="text-gray-500 hover:text-gray-700 dark:text-gray-400 dark:hover:text-gray-200"
                        type="button"
                        disabled={updateWorkoutMutation.isPending}
                      >
                        <svg
                          className="w-4 h-4"
                          fill="none"
                          stroke="currentColor"
                          viewBox="0 0 24 24"
                        >
                          <path
                            strokeLinecap="round"
                            strokeLinejoin="round"
                            strokeWidth={2}
                            d="M6 18L18 6M6 6l12 12"
                          />
                        </svg>
                      </button>
                    </div>
                  ) : (
                    <button
                      onClick={() => setIsEditingRunType(true)}
                      className="flex items-center gap-1 hover:text-blue-600 dark:hover:text-blue-400 transition-colors"
                      type="button"
                    >
                      <span>{data.runType || 'None'}</span>
                      <svg
                        className="w-4 h-4 opacity-50"
                        fill="none"
                        stroke="currentColor"
                        viewBox="0 0 24 24"
                        xmlns="http://www.w3.org/2000/svg"
                      >
                        <path
                          strokeLinecap="round"
                          strokeLinejoin="round"
                          strokeWidth={2}
                          d="M11 5H6a2 2 0 00-2 2v11a2 2 0 002 2h11a2 2 0 002-2v-5m-1.414-9.414a2 2 0 112.828 2.828L11.828 15H9v-2.828l8.586-8.586z"
                        />
                      </svg>
                    </button>
                  )}
                  {updateWorkoutMutation.isPending && (
                    <span className="ml-2 text-xs text-gray-500">Saving...</span>
                  )}
                  {updateWorkoutMutation.isError && (
                    <span className="ml-2 text-xs text-red-600 dark:text-red-400">
                      Error: {updateWorkoutMutation.error instanceof Error ? updateWorkoutMutation.error.message : 'Failed to update'}
                    </span>
                  )}
                </dd>
              </div>
              <div>
                <dt className="text-sm font-medium text-gray-600 dark:text-gray-400">Source</dt>
                <dd className="mt-1 text-sm text-gray-900 dark:text-gray-100">
                  {data.source || '—'}
                </dd>
              </div>
              <div>
                <dt className="text-sm font-medium text-gray-600 dark:text-gray-400">Started At</dt>
                <dd className="mt-1 text-sm text-gray-900 dark:text-gray-100">
                  {formatDateTime(data.startedAt)}
                </dd>
              </div>
              <div>
                <dt className="text-sm font-medium text-gray-600 dark:text-gray-400">Created At</dt>
                <dd className="mt-1 text-sm text-gray-900 dark:text-gray-100">
                  {formatDateTime(data.createdAt)}
                </dd>
              </div>
            </dl>
            <div className="mt-4">
              <dt className="text-sm font-medium text-gray-600 dark:text-gray-400 mb-1">Notes</dt>
              <dd className="mt-1">
                {isEditingNotes ? (
                  <div className="space-y-2">
                    <textarea
                      value={notesValue}
                      onChange={(e) => setNotesValue(e.target.value)}
                      disabled={updateWorkoutMutation.isPending}
                      placeholder="Add notes about this workout..."
                      rows={4}
                      className="w-full px-3 py-2 border border-gray-300 dark:border-gray-600 rounded-md bg-white dark:bg-gray-800 text-gray-900 dark:text-gray-100 placeholder-gray-400 dark:placeholder-gray-500 focus:outline-none focus:ring-2 focus:ring-blue-500 disabled:opacity-50 disabled:cursor-not-allowed resize-y"
                      autoFocus
                    />
                    <div className="flex items-center gap-2">
                      <button
                        onClick={handleSaveNotes}
                        disabled={updateWorkoutMutation.isPending}
                        className="px-3 py-1 text-sm font-medium text-white bg-blue-600 hover:bg-blue-700 dark:bg-blue-500 dark:hover:bg-blue-600 rounded-md disabled:opacity-50 disabled:cursor-not-allowed transition-colors"
                      >
                        Save
                      </button>
                      <button
                        onClick={handleCancelNotes}
                        disabled={updateWorkoutMutation.isPending}
                        className="px-3 py-1 text-sm font-medium text-gray-700 dark:text-gray-300 bg-gray-100 dark:bg-gray-700 hover:bg-gray-200 dark:hover:bg-gray-600 rounded-md disabled:opacity-50 disabled:cursor-not-allowed transition-colors"
                      >
                        Cancel
                      </button>
                      {updateWorkoutMutation.isPending && (
                        <span className="text-xs text-gray-500 dark:text-gray-400">Saving...</span>
                      )}
                      {updateWorkoutMutation.isError && (
                        <span className="text-xs text-red-600 dark:text-red-400">
                          Error: {updateWorkoutMutation.error instanceof Error ? updateWorkoutMutation.error.message : 'Failed to update'}
                        </span>
                      )}
                    </div>
                  </div>
                ) : (
                  <button
                    onClick={() => {
                      setNotesValue(data.notes || '');
                      setIsEditingNotes(true);
                    }}
                    className="flex items-start gap-2 w-full text-left hover:text-blue-600 dark:hover:text-blue-400 transition-colors group"
                    type="button"
                  >
                    <div className="flex-1 min-w-0">
                      {data.notes ? (
                        <p className="text-sm text-gray-900 dark:text-gray-100 whitespace-pre-wrap">
                          {data.notes}
                        </p>
                      ) : (
                        <p className="text-sm text-gray-400 dark:text-gray-500 italic">
                          Add notes about this workout...
                        </p>
                      )}
                    </div>
                    <svg
                      className="w-4 h-4 opacity-0 group-hover:opacity-50 mt-0.5 flex-shrink-0"
                      fill="none"
                      stroke="currentColor"
                      viewBox="0 0 24 24"
                      xmlns="http://www.w3.org/2000/svg"
                    >
                      <path
                        strokeLinecap="round"
                        strokeLinejoin="round"
                        strokeWidth={2}
                        d="M11 5H6a2 2 0 00-2 2v11a2 2 0 002 2h11a2 2 0 002-2v-5m-1.414-9.414a2 2 0 112.828 2.828L11.828 15H9v-2.828l8.586-8.586z"
                      />
                    </svg>
                  </button>
                )}
              </dd>
            </div>
            <WorkoutMediaGallery
              workoutId={id}
              media={isMediaError ? [] : media}
              isLoading={isLoadingMedia}
              onMediaClick={handleMediaClick}
            />
          </div>

          {/* Splits Table */}
          {data.splits && data.splits.length > 0 && (
            <div className="bg-white dark:bg-gray-900 p-6 rounded-lg border border-gray-200 dark:border-gray-800">
              <h2 className="text-xl font-semibold text-gray-900 dark:text-gray-100 mb-4">
                Splits ({data.splits.length})
              </h2>
              <div className="overflow-x-auto">
                <table className="w-full border-collapse">
                  <thead>
                    <tr className="border-b border-gray-200 dark:border-gray-800">
                      <th className="text-left py-3 px-4 text-sm font-semibold text-gray-700 dark:text-gray-300">
                        Split
                      </th>
                      <th className="text-left py-3 px-4 text-sm font-semibold text-gray-700 dark:text-gray-300">
                        Distance
                      </th>
                      <th className="text-left py-3 px-4 text-sm font-semibold text-gray-700 dark:text-gray-300">
                        Duration
                      </th>
                      <th className="text-left py-3 px-4 text-sm font-semibold text-gray-700 dark:text-gray-300">
                        Pace
                      </th>
                    </tr>
                  </thead>
                  <tbody>
                    {data.splits.map((split) => (
                      <tr
                        key={`split-${split.idx}`}
                        className="border-b border-gray-100 dark:border-gray-900"
                      >
                        <td className="py-3 px-4 text-gray-700 dark:text-gray-300">
                          {split.idx + 1}
                        </td>
                        <td className="py-3 px-4 text-gray-700 dark:text-gray-300">
                          {formatDistance(split.distanceM, unitPreference)}
                        </td>
                        <td className="py-3 px-4 text-gray-700 dark:text-gray-300">
                          {formatDuration(split.durationS)}
                        </td>
                        <td className="py-3 px-4 text-gray-700 dark:text-gray-300">
                          {formatPace(split.paceS, unitPreference)}
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            </div>
          )}

          {/* Weather Data */}
          {data.weather && (
            <WeatherDisplay weather={data.weather} workoutStartTime={data.startedAt} />
          )}

          {/* Route Map */}
          {data.route && (
            <div className="bg-white dark:bg-gray-900 p-6 rounded-lg border border-gray-200 dark:border-gray-800">
              <h2 className="text-xl font-semibold text-gray-900 dark:text-gray-100 mb-4">
                Route Map
              </h2>
              <WorkoutMap key={data.id} route={data.route} workoutId={data.id} />
            </div>
          )}
        </div>

        {/* Media Modal */}
        {media && media.length > 0 && selectedMediaIndex !== null && (
          <MediaModal
            media={media}
            initialIndex={selectedMediaIndex}
            workoutId={id}
            isOpen={isModalOpen}
            onClose={handleCloseModal}
          />
        )}
      </main>
    </div>
  );
}


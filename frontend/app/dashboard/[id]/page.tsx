'use client';

import { useState, useEffect } from 'react';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import Link from 'next/link';
import { useParams, useRouter } from 'next/navigation';
import dynamic from 'next/dynamic';
import Image from 'next/image';
import { getWorkout, getWorkoutMedia, updateWorkout, deleteWorkout, type WorkoutMedia } from '@/lib/api';
import { formatDate, formatDateTime, formatDistance, formatDuration, formatPace, formatElevation, getWorkoutDisplayName } from '@/lib/format';
import { useSettings } from '@/lib/settings';
import { WorkoutMediaGallery } from '@/components/WorkoutMediaGallery';
import { MediaModal } from '@/components/MediaModal';
import { MediaUpload } from '@/components/MediaUpload';
import {
  getWeatherSymbol,
  formatTemperature,
  formatWindSpeed,
  formatWindDirection,
  getFeelsLikeTemperature,
  getHumidity,
  isNightTime,
} from '@/lib/weather';

// Dynamically import WorkoutMap to avoid SSR issues with Leaflet
const WorkoutMap = dynamic(() => import('@/components/WorkoutMap'), {
  ssr: false,
  loading: () => (
    <div className="flex items-center justify-center h-64 bg-gray-100 dark:bg-gray-800 rounded-lg border border-gray-200 dark:border-gray-700">
      <p className="text-gray-500 dark:text-gray-400">Loading map...</p>
    </div>
  ),
});

export default function WorkoutDetailPage() {
  const params = useParams();
  const router = useRouter();
  const id = params.id as string;
  const [selectedMediaIndex, setSelectedMediaIndex] = useState<number | null>(null);
  const [isModalOpen, setIsModalOpen] = useState(false);
  const [isEditingRunType, setIsEditingRunType] = useState(false);
  const [isEditingNotes, setIsEditingNotes] = useState(false);
  const [notesValue, setNotesValue] = useState<string>('');
  const [hoveredSplitIdx, setHoveredSplitIdx] = useState<number | null>(null);
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

  const deleteWorkoutMutation = useMutation({
    mutationFn: () => deleteWorkout(id),
    onSuccess: () => {
      // Invalidate all workout-related queries
      queryClient.invalidateQueries({ queryKey: ['workouts'] });
      queryClient.invalidateQueries({ queryKey: ['workout', id] });
      // Redirect to dashboard
      router.push('/dashboard');
    },
  });

  const handleDeleteWorkout = () => {
    if (window.confirm('Are you sure you want to delete this workout? This action cannot be undone.')) {
      deleteWorkoutMutation.mutate();
    }
  };

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
        <main className="flex min-h-screen w-full max-w-6xl flex-col items-start py-8 px-6">
          <div className="w-full">
            <h1 className="text-3xl font-bold text-gray-900 dark:text-gray-100 mb-2">
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
        <main className="flex min-h-screen w-full max-w-6xl flex-col items-start py-8 px-6">
          <div className="w-full">
            <Link
              href="/dashboard"
              className="text-blue-600 dark:text-blue-400 hover:underline mb-4 inline-block"
            >
              ← Back to Dashboard
            </Link>
            <h1 className="text-3xl font-bold text-gray-900 dark:text-gray-100 mb-2">
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
      <main className="flex min-h-screen w-full max-w-6xl flex-col items-start py-8 px-6">
        <div className="w-full mb-4">
          <div className="flex items-center justify-between mb-2">
            <Link
              href="/dashboard"
              className="text-blue-600 dark:text-blue-400 hover:underline text-sm"
            >
              ← Back to Dashboard
            </Link>
            <div className="flex items-center gap-2">
              <button
                onClick={handleDeleteWorkout}
                disabled={deleteWorkoutMutation.isPending}
                className="px-3 py-1.5 text-sm font-medium text-red-600 dark:text-red-400 hover:text-red-700 dark:hover:text-red-300 disabled:opacity-50 disabled:cursor-not-allowed transition-colors flex items-center gap-1"
                type="button"
                aria-label="Delete workout"
              >
                {deleteWorkoutMutation.isPending ? (
                  <>
                    <svg className="animate-spin h-4 w-4" xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24">
                      <circle className="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="4"></circle>
                      <path className="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4zm2 5.291A7.962 7.962 0 014 12H0c0 3.042 1.135 5.824 3 7.938l3-2.647z"></path>
                    </svg>
                    Deleting...
                  </>
                ) : (
                  <>
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
                        d="M19 7l-.867 12.142A2 2 0 0116.138 21H7.862a2 2 0 01-1.995-1.858L5 7m5 4v6m4-6v6m1-10V4a1 1 0 00-1-1h-4a1 1 0 00-1 1v3M4 7h16"
                      />
                    </svg>
                    Delete
                  </>
                )}
              </button>
              {deleteWorkoutMutation.isError && (
                <span className="text-xs text-red-600 dark:text-red-400">
                  {deleteWorkoutMutation.error instanceof Error ? deleteWorkoutMutation.error.message : 'Failed to delete workout'}
                </span>
              )}
              <Link
                href="/settings"
                className="px-3 py-1.5 text-sm font-medium text-gray-700 dark:text-gray-300 hover:text-gray-900 dark:hover:text-gray-100"
              >
                Settings
              </Link>
            </div>
          </div>
          <h1 className="text-3xl font-bold text-gray-900 dark:text-gray-100 mb-1">
            {getWorkoutDisplayName(data.name, data.startedAt)}
          </h1>
          <p className="text-base text-gray-600 dark:text-gray-400">
            {formatDateTime(data.startedAt)}
          </p>
        </div>

        <div className="w-full space-y-4">
          {/* Main Content Area - Two Columns */}
          <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
            {/* Left Column - Activity Details */}
            <div className="bg-white dark:bg-gray-900 p-4 rounded-lg border border-gray-200 dark:border-gray-800 space-y-4">
              <div>
                <p className="text-sm text-gray-600 dark:text-gray-400">
                  {formatDateTime(data.startedAt)}
                </p>
              </div>

              {/* Notes/Description */}
              <div>
                <dt className="text-xs font-medium text-gray-600 dark:text-gray-400 mb-1">Description</dt>
                <dd>
                  {isEditingNotes ? (
                    <div className="space-y-2">
                      <textarea
                        value={notesValue}
                        onChange={(e) => setNotesValue(e.target.value)}
                        disabled={updateWorkoutMutation.isPending}
                        placeholder="Add a description..."
                        rows={3}
                        className="w-full px-2 py-1.5 text-sm border border-gray-300 dark:border-gray-600 rounded-md bg-white dark:bg-gray-800 text-gray-900 dark:text-gray-100 placeholder-gray-400 dark:placeholder-gray-500 focus:outline-none focus:ring-2 focus:ring-blue-500 disabled:opacity-50 disabled:cursor-not-allowed resize-y"
                        autoFocus
                      />
                      <div className="flex items-center gap-2">
                        <button
                          onClick={handleSaveNotes}
                          disabled={updateWorkoutMutation.isPending}
                          className="px-2 py-1 text-xs font-medium text-white bg-blue-600 hover:bg-blue-700 dark:bg-blue-500 dark:hover:bg-blue-600 rounded-md disabled:opacity-50 disabled:cursor-not-allowed transition-colors"
                        >
                          Save
                        </button>
                        <button
                          onClick={handleCancelNotes}
                          disabled={updateWorkoutMutation.isPending}
                          className="px-2 py-1 text-xs font-medium text-gray-700 dark:text-gray-300 bg-gray-100 dark:bg-gray-700 hover:bg-gray-200 dark:hover:bg-gray-600 rounded-md disabled:opacity-50 disabled:cursor-not-allowed transition-colors"
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
                            Add a description...
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

              {/* Run Type */}
              <div>
                <dt className="text-xs font-medium text-gray-600 dark:text-gray-400 mb-1">Run Type</dt>
                <dd className="text-sm text-gray-900 dark:text-gray-100">
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

              {/* Device */}
              {data.device && (
                <div>
                  <dt className="text-xs font-medium text-gray-600 dark:text-gray-400 mb-1">Device</dt>
                  <dd className="text-sm text-gray-900 dark:text-gray-100">
                    {data.device}
                  </dd>
                </div>
              )}

              {/* Source */}
              {data.source && (
                <div>
                  <dt className="text-xs font-medium text-gray-600 dark:text-gray-400 mb-1">Source</dt>
                  <dd className="text-sm text-gray-900 dark:text-gray-100">
                    {data.source}
                  </dd>
                </div>
              )}

              {/* Media Upload and Gallery */}
              <div>
                <MediaUpload
                  workoutId={id}
                  onUploadSuccess={() => {
                    queryClient.invalidateQueries({ queryKey: ['workout-media', id] });
                  }}
                />
                <div className="mt-3">
                  <WorkoutMediaGallery
                    workoutId={id}
                    media={isMediaError ? [] : media}
                    isLoading={isLoadingMedia}
                    onMediaClick={handleMediaClick}
                    onDeleteSuccess={() => {
                      queryClient.invalidateQueries({ queryKey: ['workout-media', id] });
                    }}
                  />
                </div>
              </div>
            </div>

            {/* Right Column - Stats and Weather */}
            <div className="bg-white dark:bg-gray-900 p-4 rounded-lg border border-gray-200 dark:border-gray-800 space-y-4">
              {/* Key Metrics */}
              <div>
                <h3 className="text-xs font-medium text-gray-500 dark:text-gray-400 mb-3 uppercase tracking-wide">Key Metrics</h3>
                <div className="grid grid-cols-3 gap-4">
                  <div>
                    <div className="text-xs text-gray-500 dark:text-gray-400 mb-1">Distance</div>
                    <div className="text-2xl font-bold text-gray-900 dark:text-gray-100">
                      {formatDistance(data.distanceM, unitPreference)}
                    </div>
                  </div>
                  <div>
                    <div className="text-xs text-gray-500 dark:text-gray-400 mb-1">
                      {data.movingTimeS !== null && data.movingTimeS !== data.durationS ? 'Moving Time' : 'Duration'}
                    </div>
                    <div className="text-2xl font-bold text-gray-900 dark:text-gray-100">
                      {data.movingTimeS !== null && data.movingTimeS !== data.durationS 
                        ? formatDuration(data.movingTimeS)
                        : formatDuration(data.durationS)}
                    </div>
                    {data.movingTimeS !== null && data.movingTimeS !== data.durationS && (
                      <div className="text-xs text-gray-500 dark:text-gray-400 mt-1">
                        Elapsed: {formatDuration(data.durationS)}
                      </div>
                    )}
                  </div>
                  <div>
                    <div className="text-xs text-gray-500 dark:text-gray-400 mb-1">Pace</div>
                    <div className="text-2xl font-bold text-gray-900 dark:text-gray-100">
                      {formatPace(data.avgPaceS, unitPreference)}
                    </div>
                  </div>
                </div>
              </div>

              {/* Additional Details */}
              {(data.elevGainM !== null || data.calories !== null || 
                data.maxHeartRateBpm !== null || data.avgHeartRateBpm !== null ||
                data.maxCadenceRpm !== null || data.avgCadenceRpm !== null ||
                data.maxPowerWatts !== null || data.avgPowerWatts !== null) && (
                <div>
                  <h3 className="text-xs font-medium text-gray-500 dark:text-gray-400 mb-3 uppercase tracking-wide">Additional Details</h3>
                  <div className="space-y-2">
                    {data.elevGainM !== null && (
                      <div className="flex justify-between items-center">
                        <span className="text-xs text-gray-500 dark:text-gray-400">Elevation</span>
                        <span className="text-sm font-semibold text-gray-900 dark:text-gray-100">
                          {formatElevation(data.elevGainM, unitPreference)}
                        </span>
                      </div>
                    )}
                    {data.movingTimeS !== null && data.movingTimeS !== data.durationS && (
                      <div className="flex justify-between items-center">
                        <span className="text-xs text-gray-500 dark:text-gray-400">Elapsed Time</span>
                        <span className="text-sm font-semibold text-gray-900 dark:text-gray-100">
                          {formatDuration(data.durationS)}
                        </span>
                      </div>
                    )}
                    {data.calories !== null && (
                      <div className="flex justify-between items-center">
                        <span className="text-xs text-gray-500 dark:text-gray-400">Calories</span>
                        <span className="text-sm font-semibold text-gray-900 dark:text-gray-100">
                          {data.calories}
                        </span>
                      </div>
                    )}
                    {(data.maxHeartRateBpm !== null || data.avgHeartRateBpm !== null) && (
                      <div className="flex justify-between items-center">
                        <span className="text-xs text-gray-500 dark:text-gray-400">Heart Rate</span>
                        <span className="text-sm font-semibold text-gray-900 dark:text-gray-100">
                          {data.maxHeartRateBpm !== null && data.avgHeartRateBpm !== null
                            ? `${data.maxHeartRateBpm} / ${data.avgHeartRateBpm} bpm`
                            : data.maxHeartRateBpm !== null
                            ? `${data.maxHeartRateBpm} bpm`
                            : `${data.avgHeartRateBpm} bpm`}
                        </span>
                      </div>
                    )}
                    {(data.maxCadenceRpm !== null || data.avgCadenceRpm !== null) && (
                      <div className="flex justify-between items-center">
                        <span className="text-xs text-gray-500 dark:text-gray-400">Cadence</span>
                        <span className="text-sm font-semibold text-gray-900 dark:text-gray-100">
                          {data.maxCadenceRpm !== null && data.avgCadenceRpm !== null
                            ? `${data.maxCadenceRpm} / ${data.avgCadenceRpm} rpm`
                            : data.maxCadenceRpm !== null
                            ? `${data.maxCadenceRpm} rpm`
                            : `${data.avgCadenceRpm} rpm`}
                        </span>
                      </div>
                    )}
                    {(data.maxPowerWatts !== null || data.avgPowerWatts !== null) && (
                      <div className="flex justify-between items-center">
                        <span className="text-xs text-gray-500 dark:text-gray-400">Power</span>
                        <span className="text-sm font-semibold text-gray-900 dark:text-gray-100">
                          {data.maxPowerWatts !== null && data.avgPowerWatts !== null
                            ? `${data.maxPowerWatts} / ${data.avgPowerWatts} W`
                            : data.maxPowerWatts !== null
                            ? `${data.maxPowerWatts} W`
                            : `${data.avgPowerWatts} W`}
                        </span>
                      </div>
                    )}
                  </div>
                </div>
              )}

              {/* Weather Information */}
              {data.weather && (() => {
                const isNight = isNightTime(data.startedAt);
                const symbolFilename = getWeatherSymbol(data.weather.weatherCode, isNight);
                const symbolPath = `/weather-symbols/${symbolFilename}`;
                const conditionText = data.weather.condition || 'Unknown';
                const feelsLike = getFeelsLikeTemperature(data.weather);
                const humidity = getHumidity(data.weather);

                return (
                  <div>
                    <h3 className="text-xs font-medium text-gray-500 dark:text-gray-400 mb-3 uppercase tracking-wide">Weather</h3>
                    <div className="space-y-2">
                      <div className="flex justify-between items-center">
                        <span className="text-xs text-gray-500 dark:text-gray-400 flex items-center gap-1.5">
                          <div className="relative w-4 h-4 flex-shrink-0">
                            <Image
                              src={symbolPath}
                              alt={conditionText}
                              fill
                              className="object-contain"
                              unoptimized
                            />
                          </div>
                          Condition
                        </span>
                        <span className="text-sm font-semibold text-gray-900 dark:text-gray-100">
                          {conditionText}
                        </span>
                      </div>
                      <div className="flex justify-between items-center">
                        <span className="text-xs text-gray-500 dark:text-gray-400">Temperature</span>
                        <span className="text-sm font-semibold text-gray-900 dark:text-gray-100">
                          {formatTemperature(data.weather.temperature, unitPreference)}
                        </span>
                      </div>
                      {humidity !== undefined && humidity !== null && (
                        <div className="flex justify-between items-center">
                          <span className="text-xs text-gray-500 dark:text-gray-400">Humidity</span>
                          <span className="text-sm font-semibold text-gray-900 dark:text-gray-100">
                            {Math.round(humidity)}%
                          </span>
                        </div>
                      )}
                      {feelsLike !== undefined && (
                        <div className="flex justify-between items-center">
                          <span className="text-xs text-gray-500 dark:text-gray-400">Feels like</span>
                          <span className="text-sm font-semibold text-gray-900 dark:text-gray-100">
                            {formatTemperature(feelsLike, unitPreference)}
                          </span>
                        </div>
                      )}
                      {data.weather.windSpeed !== undefined && (
                        <div className="flex justify-between items-center">
                          <span className="text-xs text-gray-500 dark:text-gray-400">Wind Speed</span>
                          <span className="text-sm font-semibold text-gray-900 dark:text-gray-100">
                            {formatWindSpeed(data.weather.windSpeed, unitPreference)}
                          </span>
                        </div>
                      )}
                      {data.weather.windDirection !== undefined && data.weather.windDirection !== null && (
                        <div className="flex justify-between items-center">
                          <span className="text-xs text-gray-500 dark:text-gray-400">Wind Direction</span>
                          <span className="text-sm font-semibold text-gray-900 dark:text-gray-100">
                            {formatWindDirection(data.weather.windDirection)} ({Math.round(data.weather.windDirection)}°)
                          </span>
                        </div>
                      )}
                    </div>
                  </div>
                );
              })()}
            </div>
          </div>

          {/* Lower Section - Splits and Map */}
          {(data.splits && data.splits.length > 0) || data.route ? (
            <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
              {/* Splits Table */}
              {data.splits && data.splits.length > 0 && (
                <div className="bg-white dark:bg-gray-900 p-4 rounded-lg border border-gray-200 dark:border-gray-800">
                  <h2 className="text-lg font-semibold text-gray-900 dark:text-gray-100 mb-3">
                    Splits ({data.splits.length})
                  </h2>
                  <div className="overflow-x-auto">
                    <table className="w-full border-collapse">
                      <thead>
                        <tr className="border-b border-gray-200 dark:border-gray-800">
                          <th className="text-left py-2 px-3 text-xs font-semibold text-gray-700 dark:text-gray-300">
                            Split
                          </th>
                          <th className="text-left py-2 px-3 text-xs font-semibold text-gray-700 dark:text-gray-300">
                            Distance
                          </th>
                          <th className="text-left py-2 px-3 text-xs font-semibold text-gray-700 dark:text-gray-300">
                            Duration
                          </th>
                          <th className="text-left py-2 px-3 text-xs font-semibold text-gray-700 dark:text-gray-300">
                            Pace
                          </th>
                        </tr>
                      </thead>
                      <tbody>
                        {data.splits.map((split) => (
                          <tr
                            key={`split-${split.idx}`}
                            className="border-b border-gray-100 dark:border-gray-900 cursor-pointer hover:bg-gray-50 dark:hover:bg-gray-800 transition-colors"
                            onMouseEnter={() => setHoveredSplitIdx(split.idx)}
                            onMouseLeave={() => setHoveredSplitIdx(null)}
                          >
                            <td className="py-2 px-3 text-xs text-gray-700 dark:text-gray-300">
                              {split.idx + 1}
                            </td>
                            <td className="py-2 px-3 text-xs text-gray-700 dark:text-gray-300">
                              {formatDistance(split.distanceM, unitPreference)}
                            </td>
                            <td className="py-2 px-3 text-xs text-gray-700 dark:text-gray-300">
                              {formatDuration(split.durationS)}
                            </td>
                            <td className="py-2 px-3 text-xs text-gray-700 dark:text-gray-300">
                              {formatPace(split.paceS, unitPreference)}
                            </td>
                          </tr>
                        ))}
                      </tbody>
                    </table>
                  </div>
                </div>
              )}

              {/* Route Map */}
              {data.route && (
                <div className="bg-white dark:bg-gray-900 p-4 rounded-lg border border-gray-200 dark:border-gray-800">
                  <h2 className="text-lg font-semibold text-gray-900 dark:text-gray-100 mb-3">
                    Route Map
                  </h2>
                  <WorkoutMap 
                    key={data.id} 
                    route={data.route} 
                    workoutId={data.id}
                    splits={data.splits}
                    hoveredSplitIdx={hoveredSplitIdx}
                  />
                </div>
              )}
            </div>
          ) : null}
        </div>

        {/* Media Modal */}
        {media && media.length > 0 && selectedMediaIndex !== null && (
          <MediaModal
            media={media}
            initialIndex={selectedMediaIndex}
            workoutId={id}
            isOpen={isModalOpen}
            onClose={handleCloseModal}
            onDeleteSuccess={() => {
              queryClient.invalidateQueries({ queryKey: ['workout-media', id] });
              // If modal closes after deleting last item, reset state
              if (media && media.length <= 1) {
                setIsModalOpen(false);
                setSelectedMediaIndex(null);
              }
            }}
          />
        )}
      </main>
    </div>
  );
}


'use client';

import { useEffect } from 'react';
import { useMutation } from '@tanstack/react-query';
import { getWorkoutMediaUrl, deleteWorkoutMedia, type WorkoutMedia } from '@/lib/api';

interface WorkoutMediaGalleryProps {
  workoutId: string;
  media: WorkoutMedia[] | undefined;
  isLoading?: boolean;
  onMediaClick: (media: WorkoutMedia, index: number) => void;
  onDeleteSuccess?: () => void;
}

export function WorkoutMediaGallery({ workoutId, media, isLoading, onMediaClick, onDeleteSuccess }: WorkoutMediaGalleryProps) {
  // All hooks must be called before any conditional returns
  const deleteMutation = useMutation({
    mutationFn: (mediaId: string) => deleteWorkoutMedia(workoutId, mediaId),
    onSuccess: () => {
      onDeleteSuccess?.();
    },
  });

  // Debug logging for component props
  useEffect(() => {
    console.log('[WorkoutMediaGallery] Component props:', {
      workoutId,
      isLoading,
      media,
      mediaType: typeof media,
      isArray: Array.isArray(media),
      length: Array.isArray(media) ? media.length : 'N/A',
      mediaContents: Array.isArray(media) ? media : 'Not an array',
    });
  }, [workoutId, media, isLoading]);

  const handleDelete = (e: React.MouseEvent, mediaId: string, filename: string) => {
    e.stopPropagation();
    if (confirm(`Are you sure you want to delete "${filename}"? This action cannot be undone.`)) {
      deleteMutation.mutate(mediaId);
    }
  };

  // Show loading state
  if (isLoading) {
    return (
      <div className="mt-4">
        <dt className="text-sm font-medium text-gray-600 dark:text-gray-400 mb-2">Media</dt>
        <dd className="text-sm text-gray-500 dark:text-gray-400">Loading media...</dd>
      </div>
    );
  }

  // If media is undefined, we're still waiting for data (shouldn't happen if isLoading is false, but be defensive)
  if (media === undefined) {
    return null;
  }

  // If no media items, don't show the section
  if (!Array.isArray(media) || media.length === 0) {
    console.log('[WorkoutMediaGallery] Returning null - no media items', {
      isArray: Array.isArray(media),
      length: Array.isArray(media) ? media.length : 'N/A',
    });
    return null;
  }

  const isImage = (mimeType: string) => mimeType.startsWith('image/');
  const isVideo = (mimeType: string) => mimeType.startsWith('video/');

  console.log('[WorkoutMediaGallery] Rendering gallery with', media.length, 'items');

  return (
    <div className="mt-4">
      <dt className="text-sm font-medium text-gray-600 dark:text-gray-400 mb-2">
        Media {process.env.NODE_ENV === 'development' && `(${media.length} items)`}
      </dt>
      <dd>
        <div className="grid grid-cols-2 md:grid-cols-3 lg:grid-cols-4 gap-2">
          {media.map((item, index) => (
            <div
              key={item.id}
              className="relative aspect-square rounded-lg overflow-hidden border border-gray-200 dark:border-gray-700 hover:border-gray-300 dark:hover:border-gray-600 hover:shadow-md transition-all group"
            >
              <button
                onClick={() => onMediaClick(item, index)}
                className="absolute inset-0 w-full h-full cursor-pointer"
              >
                {isImage(item.mimeType) ? (
                <img
                  src={getWorkoutMediaUrl(workoutId, item.id)}
                  alt={item.caption || item.filename}
                  className="w-full h-full object-cover"
                  loading="lazy"
                />
              ) : isVideo(item.mimeType) ? (
                <div className="w-full h-full bg-gray-100 dark:bg-gray-800 flex items-center justify-center">
                  <video
                    src={getWorkoutMediaUrl(workoutId, item.id)}
                    className="w-full h-full object-cover"
                    preload="metadata"
                  />
                  <div className="absolute inset-0 flex items-center justify-center bg-black bg-opacity-30 group-hover:bg-opacity-40 transition-opacity">
                    <svg
                      className="w-12 h-12 text-white opacity-80"
                      fill="currentColor"
                      viewBox="0 0 24 24"
                    >
                      <path d="M8 5v14l11-7z" />
                    </svg>
                  </div>
                </div>
              ) : (
                <div className="w-full h-full bg-gray-100 dark:bg-gray-800 flex items-center justify-center">
                  <svg
                    className="w-8 h-8 text-gray-400"
                    fill="none"
                    stroke="currentColor"
                    viewBox="0 0 24 24"
                  >
                    <path
                      strokeLinecap="round"
                      strokeLinejoin="round"
                      strokeWidth={2}
                      d="M7 21h10a2 2 0 002-2V9.414a1 1 0 00-.293-.707l-5.414-5.414A1 1 0 0012.586 3H7a2 2 0 00-2 2v14a2 2 0 002 2z"
                    />
                  </svg>
                </div>
              )}
              </button>
              {/* Delete button - appears on hover */}
              <button
                onClick={(e) => handleDelete(e, item.id, item.filename)}
                disabled={deleteMutation.isPending}
                className="absolute top-2 right-2 p-1.5 bg-red-600 hover:bg-red-700 text-white rounded-full opacity-0 group-hover:opacity-100 transition-opacity disabled:opacity-50 disabled:cursor-not-allowed z-10"
                aria-label="Delete media"
                title="Delete media"
              >
                {deleteMutation.isPending ? (
                  <svg
                    className="w-4 h-4 animate-spin"
                    fill="none"
                    viewBox="0 0 24 24"
                  >
                    <circle
                      className="opacity-25"
                      cx="12"
                      cy="12"
                      r="10"
                      stroke="currentColor"
                      strokeWidth="4"
                    />
                    <path
                      className="opacity-75"
                      fill="currentColor"
                      d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4zm2 5.291A7.962 7.962 0 014 12H0c0 3.042 1.135 5.824 3 7.938l3-2.647z"
                    />
                  </svg>
                ) : (
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
                      d="M19 7l-.867 12.142A2 2 0 0116.138 21H7.862a2 2 0 01-1.995-1.858L5 7m5 4v6m4-6v6m1-10V4a1 1 0 00-1-1h-4a1 1 0 00-1 1v3M4 7h16"
                    />
                  </svg>
                )}
              </button>
            </div>
          ))}
        </div>
      </dd>
    </div>
  );
}


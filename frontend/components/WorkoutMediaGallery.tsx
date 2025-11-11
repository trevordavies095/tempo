'use client';

import { useEffect } from 'react';
import { getWorkoutMediaUrl, type WorkoutMedia } from '@/lib/api';

interface WorkoutMediaGalleryProps {
  workoutId: string;
  media: WorkoutMedia[] | undefined;
  isLoading?: boolean;
  onMediaClick: (media: WorkoutMedia, index: number) => void;
}

export function WorkoutMediaGallery({ workoutId, media, isLoading, onMediaClick }: WorkoutMediaGalleryProps) {
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
            <button
              key={item.id}
              onClick={() => onMediaClick(item, index)}
              className="relative aspect-square rounded-lg overflow-hidden border border-gray-200 dark:border-gray-700 hover:border-gray-300 dark:hover:border-gray-600 hover:shadow-md transition-all cursor-pointer group"
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
          ))}
        </div>
      </dd>
    </div>
  );
}


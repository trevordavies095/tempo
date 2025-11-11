'use client';

import { useEffect, useState } from 'react';
import { type WorkoutMedia } from '@/lib/api';
import { getWorkoutMediaUrl } from '@/lib/api';

interface MediaModalProps {
  media: WorkoutMedia[];
  initialIndex: number;
  workoutId: string;
  isOpen: boolean;
  onClose: () => void;
}

export function MediaModal({
  media,
  initialIndex,
  workoutId,
  isOpen,
  onClose,
}: MediaModalProps) {
  const [currentIndex, setCurrentIndex] = useState(initialIndex);
  const [isLoading, setIsLoading] = useState(true);

  useEffect(() => {
    setCurrentIndex(initialIndex);
    setIsLoading(true);
  }, [initialIndex, isOpen]);

  useEffect(() => {
    if (!isOpen) return;

    const handleKeyDown = (e: KeyboardEvent) => {
      if (e.key === 'Escape') {
        onClose();
      } else if (e.key === 'ArrowLeft') {
        e.preventDefault();
        setCurrentIndex((prev) => (prev > 0 ? prev - 1 : media.length - 1));
      } else if (e.key === 'ArrowRight') {
        e.preventDefault();
        setCurrentIndex((prev) => (prev < media.length - 1 ? prev + 1 : 0));
      }
    };

    window.addEventListener('keydown', handleKeyDown);
    return () => window.removeEventListener('keydown', handleKeyDown);
  }, [isOpen, media.length, onClose]);

  if (!isOpen || media.length === 0) {
    return null;
  }

  const currentMedia = media[currentIndex];
  const isImage = currentMedia.mimeType.startsWith('image/');
  const isVideo = currentMedia.mimeType.startsWith('video/');
  const mediaUrl = getWorkoutMediaUrl(workoutId, currentMedia.id);

  const handlePrevious = (e: React.MouseEvent) => {
    e.stopPropagation();
    setCurrentIndex((prev) => (prev > 0 ? prev - 1 : media.length - 1));
    setIsLoading(true);
  };

  const handleNext = (e: React.MouseEvent) => {
    e.stopPropagation();
    setCurrentIndex((prev) => (prev < media.length - 1 ? prev + 1 : 0));
    setIsLoading(true);
  };

  const handleBackdropClick = (e: React.MouseEvent) => {
    if (e.target === e.currentTarget) {
      onClose();
    }
  };

  return (
    <div
      className="fixed inset-0 z-50 flex items-center justify-center bg-black bg-opacity-75 p-4"
      onClick={handleBackdropClick}
    >
      <div className="relative max-w-7xl max-h-[90vh] w-full h-full flex items-center justify-center">
        {/* Close button */}
        <button
          onClick={onClose}
          className="absolute top-4 right-4 z-10 p-2 bg-black bg-opacity-50 hover:bg-opacity-70 rounded-full text-white transition-colors"
          aria-label="Close"
        >
          <svg
            className="w-6 h-6"
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

        {/* Navigation arrows */}
        {media.length > 1 && (
          <>
            <button
              onClick={handlePrevious}
              className="absolute left-4 z-10 p-3 bg-black bg-opacity-50 hover:bg-opacity-70 rounded-full text-white transition-colors"
              aria-label="Previous"
            >
              <svg
                className="w-6 h-6"
                fill="none"
                stroke="currentColor"
                viewBox="0 0 24 24"
              >
                <path
                  strokeLinecap="round"
                  strokeLinejoin="round"
                  strokeWidth={2}
                  d="M15 19l-7-7 7-7"
                />
              </svg>
            </button>
            <button
              onClick={handleNext}
              className="absolute right-4 z-10 p-3 bg-black bg-opacity-50 hover:bg-opacity-70 rounded-full text-white transition-colors"
              aria-label="Next"
            >
              <svg
                className="w-6 h-6"
                fill="none"
                stroke="currentColor"
                viewBox="0 0 24 24"
              >
                <path
                  strokeLinecap="round"
                  strokeLinejoin="round"
                  strokeWidth={2}
                  d="M9 5l7 7-7 7"
                />
              </svg>
            </button>
          </>
        )}

        {/* Media content */}
        <div className="relative w-full h-full flex items-center justify-center">
          {isLoading && (
            <div className="absolute inset-0 flex items-center justify-center bg-black bg-opacity-50">
              <div className="text-white">Loading...</div>
            </div>
          )}
          {isImage ? (
            <img
              src={mediaUrl}
              alt={currentMedia.caption || currentMedia.filename}
              className="max-w-full max-h-[90vh] object-contain"
              onLoad={() => setIsLoading(false)}
              onError={() => setIsLoading(false)}
            />
          ) : isVideo ? (
            <video
              src={mediaUrl}
              controls
              className="max-w-full max-h-[90vh]"
              onLoadedData={() => setIsLoading(false)}
              onError={() => setIsLoading(false)}
            />
          ) : (
            <div className="bg-gray-800 p-8 rounded-lg text-white">
              <p>Unsupported media type: {currentMedia.mimeType}</p>
            </div>
          )}
        </div>

        {/* Caption */}
        {currentMedia.caption && (
          <div className="absolute bottom-4 left-1/2 transform -translate-x-1/2 bg-black bg-opacity-70 text-white px-4 py-2 rounded-lg max-w-2xl text-center">
            {currentMedia.caption}
          </div>
        )}

        {/* Media counter */}
        {media.length > 1 && (
          <div className="absolute top-4 left-1/2 transform -translate-x-1/2 bg-black bg-opacity-50 text-white px-4 py-2 rounded-lg text-sm">
            {currentIndex + 1} / {media.length}
          </div>
        )}
      </div>
    </div>
  );
}


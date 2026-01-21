'use client';

import { useEffect, useState, useCallback } from 'react';
import { useMutation } from '@tanstack/react-query';
import { type WorkoutMedia, deleteWorkoutMedia } from '@/lib/api';
import { getWorkoutMediaUrl } from '@/lib/api';
import { IconX, IconTrash, IconChevronLeft, IconChevronRight, IconLoader2 } from '@tabler/icons-react';

interface MediaModalProps {
  media: WorkoutMedia[];
  initialIndex: number;
  workoutId: string;
  isOpen: boolean;
  onClose: () => void;
  onDeleteSuccess?: () => void;
}

export function MediaModal({
  media,
  initialIndex,
  workoutId,
  isOpen,
  onClose,
  onDeleteSuccess,
}: MediaModalProps) {
  const [currentIndex, setCurrentIndex] = useState(initialIndex);
  const [isLoading, setIsLoading] = useState(true);
  const [mediaList, setMediaList] = useState(media);

  // Update media list when prop changes
  useEffect(() => {
    setMediaList(media);
  }, [media]);

  const deleteMutation = useMutation({
    mutationFn: (mediaId: string) => deleteWorkoutMedia(workoutId, mediaId),
    onSuccess: () => {
      const deletedMedia = mediaList[currentIndex];
      const newMediaList = mediaList.filter((m) => m.id !== deletedMedia.id);
      setMediaList(newMediaList);

      if (newMediaList.length === 0) {
        // No more media, close modal
        onClose();
      } else {
        // Navigate to next item, or previous if we deleted the last item
        const newIndex = currentIndex >= newMediaList.length ? newMediaList.length - 1 : currentIndex;
        setCurrentIndex(newIndex);
        setIsLoading(true);
      }

      onDeleteSuccess?.();
    },
  });

  const handleDelete = useCallback(() => {
    const currentMedia = mediaList[currentIndex];
    if (currentMedia && confirm(`Are you sure you want to delete "${currentMedia.filename}"? This action cannot be undone.`)) {
      deleteMutation.mutate(currentMedia.id);
    }
  }, [mediaList, currentIndex, deleteMutation]);

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
        setCurrentIndex((prev) => (prev > 0 ? prev - 1 : mediaList.length - 1));
      } else if (e.key === 'ArrowRight') {
        e.preventDefault();
        setCurrentIndex((prev) => (prev < mediaList.length - 1 ? prev + 1 : 0));
      } else if (e.key === 'Delete' || e.key === 'Backspace') {
        e.preventDefault();
        handleDelete();
      }
    };

    window.addEventListener('keydown', handleKeyDown);
    return () => window.removeEventListener('keydown', handleKeyDown);
  }, [isOpen, mediaList.length, onClose, handleDelete]);

  if (!isOpen || mediaList.length === 0) {
    return null;
  }

  const currentMedia = mediaList[currentIndex];
  const isImage = currentMedia.mimeType.startsWith('image/');
  const isVideo = currentMedia.mimeType.startsWith('video/');
  const mediaUrl = getWorkoutMediaUrl(workoutId, currentMedia.id);

  const handlePrevious = (e: React.MouseEvent) => {
    e.stopPropagation();
    setCurrentIndex((prev) => (prev > 0 ? prev - 1 : mediaList.length - 1));
    setIsLoading(true);
  };

  const handleNext = (e: React.MouseEvent) => {
    e.stopPropagation();
    setCurrentIndex((prev) => (prev < mediaList.length - 1 ? prev + 1 : 0));
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
          <IconX className="w-6 h-6" />
        </button>

        {/* Delete button */}
        <button
          onClick={handleDelete}
          disabled={deleteMutation.isPending}
          className="absolute top-4 right-20 z-10 p-2 bg-red-600 hover:bg-red-700 disabled:bg-red-800 disabled:opacity-50 rounded-full text-white transition-colors"
          aria-label="Delete media"
          title="Delete media (Delete key)"
        >
          {deleteMutation.isPending ? (
            <IconLoader2 className="w-6 h-6 animate-spin" />
          ) : (
            <IconTrash className="w-6 h-6" />
          )}
        </button>

        {/* Navigation arrows */}
        {mediaList.length > 1 && (
          <>
            <button
              onClick={handlePrevious}
              className="absolute left-4 z-10 p-3 bg-black bg-opacity-50 hover:bg-opacity-70 rounded-full text-white transition-colors"
              aria-label="Previous"
            >
              <IconChevronLeft className="w-6 h-6" />
            </button>
            <button
              onClick={handleNext}
              className="absolute right-4 z-10 p-3 bg-black bg-opacity-50 hover:bg-opacity-70 rounded-full text-white transition-colors"
              aria-label="Next"
            >
              <IconChevronRight className="w-6 h-6" />
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
        {mediaList.length > 1 && (
          <div className="absolute top-4 left-1/2 transform -translate-x-1/2 bg-black bg-opacity-50 text-white px-4 py-2 rounded-lg text-sm">
            {currentIndex + 1} / {mediaList.length}
          </div>
        )}
      </div>
    </div>
  );
}


'use client';

import { useState } from 'react';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { type WorkoutMedia } from '@/lib/api';
import { invalidateAfter, workoutQueries } from '@/lib/queries';
import { WorkoutMediaGallery } from '@/components/WorkoutMediaGallery';
import { MediaModal } from '@/components/MediaModal';
import { MediaUpload } from '@/components/MediaUpload';

export function WorkoutOverviewMedia({ workoutId }: { workoutId: string }) {
  const queryClient = useQueryClient();
  const [selectedMediaIndex, setSelectedMediaIndex] = useState<number | null>(null);
  const [isModalOpen, setIsModalOpen] = useState(false);

  const { data: media, isLoading: isLoadingMedia, isError: isMediaError } = useQuery({
    ...workoutQueries.media(workoutId),
    enabled: !!workoutId,
    retry: false,
  });

  const invalidateMedia = () => {
    invalidateAfter(queryClient, { kind: 'workoutMediaChanged', workoutId });
  };

  return (
    <div>
      <MediaUpload workoutId={workoutId} onUploadSuccess={invalidateMedia} />
      <div className="mt-2">
        <WorkoutMediaGallery
          workoutId={workoutId}
          media={isMediaError ? [] : media}
          isLoading={isLoadingMedia}
          onMediaClick={(_item: WorkoutMedia, index: number) => {
            setSelectedMediaIndex(index);
            setIsModalOpen(true);
          }}
          onDeleteSuccess={invalidateMedia}
        />
      </div>
      {media && media.length > 0 && selectedMediaIndex !== null && (
        <MediaModal
          media={media}
          initialIndex={selectedMediaIndex}
          workoutId={workoutId}
          isOpen={isModalOpen}
          onClose={() => {
            setIsModalOpen(false);
            setSelectedMediaIndex(null);
          }}
          onDeleteSuccess={() => {
            invalidateMedia();
            if (media.length <= 1) {
              setIsModalOpen(false);
              setSelectedMediaIndex(null);
            }
          }}
        />
      )}
    </div>
  );
}

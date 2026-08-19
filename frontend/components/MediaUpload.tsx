'use client';

import { useMutation } from '@tanstack/react-query';
import { useCallback, useState, useEffect, useRef } from 'react';
import { uploadWorkoutMedia } from '@/lib/api';
import { IconPlus, IconX } from '@tabler/icons-react';
import { Button } from '@/components/ui/Button';

interface MediaUploadProps {
  workoutId: string;
  onUploadSuccess?: () => void;
}

export function MediaUpload({ workoutId, onUploadSuccess }: MediaUploadProps) {
  const [selectedFiles, setSelectedFiles] = useState<File[]>([]);
  const fileInputRef = useRef<HTMLInputElement>(null);

  const mutation = useMutation({
    mutationFn: (files: File[]) => uploadWorkoutMedia(workoutId, files),
    onSuccess: () => {
      setSelectedFiles([]);
      onUploadSuccess?.();
    },
  });

  // Clear success message after 3 seconds
  useEffect(() => {
    if (mutation.isSuccess) {
      const timer = setTimeout(() => {
        mutation.reset();
      }, 3000);
      return () => clearTimeout(timer);
    }
  }, [mutation.isSuccess, mutation]);

  const handleButtonClick = useCallback(() => {
    fileInputRef.current?.click();
  }, []);

  const handleFileInput = useCallback((e: React.ChangeEvent<HTMLInputElement>) => {
    if (e.target.files && e.target.files.length > 0) {
      const files = Array.from(e.target.files);
      const validFiles = files.filter((file) => {
        const extension = file.name.toLowerCase().split('.').pop();
        const validExtensions = ['jpg', 'jpeg', 'png', 'gif', 'webp', 'mp4', 'mov', 'avi'];
        return validExtensions.includes(extension || '');
      });

      if (validFiles.length !== files.length) {
        alert('Some files were skipped. Only images (JPG, PNG, GIF, WEBP) and videos (MP4, MOV, AVI) are supported.');
      }

      if (validFiles.length > 0) {
        setSelectedFiles((prev) => [...prev, ...validFiles]);
      }
    }
    // Reset input so same file can be selected again
    if (fileInputRef.current) {
      fileInputRef.current.value = '';
    }
  }, []);

  const handleRemoveFile = useCallback((index: number) => {
    setSelectedFiles((prev) => prev.filter((_, i) => i !== index));
  }, []);

  const handleSubmit = useCallback(
    (e: React.FormEvent) => {
      e.preventDefault();
      if (selectedFiles.length > 0) {
        mutation.mutate(selectedFiles);
      }
    },
    [selectedFiles, mutation]
  );

  const formatFileSize = (bytes: number): string => {
    if (bytes < 1024) return bytes + ' B';
    if (bytes < 1024 * 1024) return (bytes / 1024).toFixed(1) + ' KB';
    return (bytes / (1024 * 1024)).toFixed(1) + ' MB';
  };

  return (
    <div className="mt-4">
      <form onSubmit={handleSubmit} className="space-y-3">
        {/* Minimal button - always visible */}
        <button
          type="button"
          onClick={handleButtonClick}
          className="text-sm text-muted hover:text-ink transition-colors flex items-center gap-1.5"
        >
          <IconPlus className="w-4 h-4" />
          <span>Add Media</span>
        </button>

        {/* Hidden file input */}
        <input
          ref={fileInputRef}
          type="file"
          accept="image/jpeg,image/png,image/gif,image/webp,video/mp4,video/quicktime,video/x-msvideo"
          multiple
          onChange={handleFileInput}
          className="hidden"
        />

        {/* File list and upload button - only shown when files are selected */}
        {selectedFiles.length > 0 && (
          <div className="space-y-2">
            <div className="space-y-1 max-h-40 overflow-y-auto">
              {selectedFiles.map((file, index) => (
                <div
                  key={`${file.name}-${index}`}
                  className="flex items-center justify-between p-2 bg-canvas rounded border border-border"
                >
                  <div className="flex-1 min-w-0">
                    <p className="text-sm text-ink truncate">
                      {file.name}
                    </p>
                    <p className="text-xs text-muted">
                      {formatFileSize(file.size)}
                    </p>
                  </div>
                  <button
                    type="button"
                    onClick={() => handleRemoveFile(index)}
                    className="ml-2 text-muted hover:text-danger transition-colors"
                  >
                    <IconX className="w-5 h-5" />
                  </button>
                </div>
              ))}
            </div>
            <Button
              type="submit"
              disabled={mutation.isPending}
              className="w-full"
            >
              {mutation.isPending ? 'Uploading...' : `Upload ${selectedFiles.length} file${selectedFiles.length > 1 ? 's' : ''}`}
            </Button>
          </div>
        )}

        {/* Success/Error messages */}
        {mutation.isSuccess && (
          <div className="p-3 bg-canvas border border-border rounded-tempo">
            <p className="text-sm text-ink">
              Media uploaded successfully!
            </p>
          </div>
        )}

        {mutation.isError && (
          <div className="p-3 bg-canvas border border-danger rounded-tempo">
            <p className="text-sm text-danger">
              Error: {mutation.error instanceof Error ? mutation.error.message : 'Unknown error'}
            </p>
          </div>
        )}
      </form>
    </div>
  );
}


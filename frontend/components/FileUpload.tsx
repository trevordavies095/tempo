'use client';

import { useMutation, useQueryClient } from '@tanstack/react-query';
import { useCallback, useState } from 'react';
import { importWorkoutFile } from '@/lib/api';
import { useSettings } from '@/lib/settings';
import { invalidateWorkoutQueries } from '@/lib/queryUtils';
import { useFileDrop } from '@/hooks/useFileDrop';

export function FileUpload() {
  const [selectedFiles, setSelectedFiles] = useState<File[]>([]);
  const { unitPreference } = useSettings();
  const queryClient = useQueryClient();

  const { dragActive, handleDrag, handleDrop, handleFileInput } = useFileDrop({
    onFilesSelected: (files) => {
      setSelectedFiles((prev) => [...prev, ...files]);
    },
    acceptExtensions: ['.gpx', '.fit', '.fit.gz'],
  });

  const mutation = useMutation({
    mutationFn: (files: File[]) => importWorkoutFile(files, unitPreference),
    onSuccess: (data) => {
      invalidateWorkoutQueries(queryClient);
      
      // Handle both single file (backward compat) and multiple file responses
      if ('totalProcessed' in data) {
        // Multiple file response
        const summary = data as { totalProcessed: number; successful: number; skipped: number; updated: number; errors: number; errorDetails: Array<{ filename: string; error: string }> };
        const message = `Import complete!\n\nTotal: ${summary.totalProcessed}\nSuccessful: ${summary.successful}\nUpdated: ${summary.updated}\nSkipped: ${summary.skipped}\nErrors: ${summary.errors}`;
        if (summary.errorDetails.length > 0) {
          const errorList = summary.errorDetails.map(e => `- ${e.filename}: ${e.error}`).join('\n');
          alert(`${message}\n\nErrors:\n${errorList}`);
        } else {
          alert(message);
        }
      } else {
        // Single file response (backward compat)
        const single = data as { distanceM: number; durationS: number };
        alert(`Workout imported successfully!\nDistance: ${(single.distanceM / 1000).toFixed(2)} km\nDuration: ${Math.floor(single.durationS / 60)}:${(single.durationS % 60).toString().padStart(2, '0')}`);
      }
      setSelectedFiles([]);
    },
    onError: (error: Error) => {
      alert(`Error importing files: ${error.message}`);
    },
  });


  const handleRemoveFile = useCallback((index: number) => {
    setSelectedFiles(prev => prev.filter((_, i) => i !== index));
  }, []);

  const formatFileSize = useCallback((bytes: number): string => {
    if (bytes < 1024) return bytes + ' B';
    if (bytes < 1024 * 1024) return (bytes / 1024).toFixed(1) + ' KB';
    return (bytes / (1024 * 1024)).toFixed(1) + ' MB';
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

  return (
    <div className="w-full max-w-2xl">
      <form onSubmit={handleSubmit} className="space-y-4">
        <div
          onDragEnter={handleDrag}
          onDragLeave={handleDrag}
          onDragOver={handleDrag}
          onDrop={handleDrop}
          className={`relative border-2 border-dashed rounded-lg p-8 transition-colors ${
            dragActive
              ? 'border-blue-500 bg-blue-50 dark:bg-blue-950'
              : 'border-gray-300 dark:border-gray-700 bg-gray-50 dark:bg-gray-900'
          }`}
        >
          <input
            type="file"
            id="file-upload"
            accept=".gpx,.fit,.fit.gz"
            multiple
            onChange={handleFileInput}
            className="absolute inset-0 w-full h-full opacity-0 cursor-pointer"
          />
          <div className="text-center">
            <svg
              className="mx-auto h-12 w-12 text-gray-400"
              stroke="currentColor"
              fill="none"
              viewBox="0 0 48 48"
            >
              <path
                d="M28 8H12a4 4 0 00-4 4v20m32-12v8m0 0v8a4 4 0 01-4 4H12a4 4 0 01-4-4v-4m32-4l-3.172-3.172a4 4 0 00-5.656 0L28 28M8 32l9.172-9.172a4 4 0 015.656 0L28 28m0 0l4 4m4-4h12m-4-4v12m0 0l-4-4m4 4l-4-4"
                strokeWidth={2}
                strokeLinecap="round"
                strokeLinejoin="round"
              />
            </svg>
            <p className="mt-2 text-sm text-gray-600 dark:text-gray-400">
              <span className="font-semibold">Click to upload</span> or drag and drop
            </p>
            <p className="text-xs text-gray-500 dark:text-gray-500">GPX or FIT files (multiple files supported)</p>
          </div>
        </div>

        {selectedFiles.length > 0 && (
          <div className="space-y-2">
            <div className="space-y-1 max-h-60 overflow-y-auto">
              {selectedFiles.map((file, index) => (
                <div
                  key={`${file.name}-${index}`}
                  className="flex items-center justify-between p-2 bg-white dark:bg-gray-800 rounded border border-gray-200 dark:border-gray-700"
                >
                  <div className="flex-1 min-w-0">
                    <p className="text-sm text-gray-900 dark:text-gray-100 truncate">
                      {file.name}
                    </p>
                    <p className="text-xs text-gray-500 dark:text-gray-400">
                      {formatFileSize(file.size)}
                    </p>
                  </div>
                  <button
                    type="button"
                    onClick={() => handleRemoveFile(index)}
                    className="ml-2 text-gray-400 hover:text-red-600 dark:hover:text-red-400 transition-colors"
                  >
                    <svg
                      className="w-5 h-5"
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
              ))}
            </div>
            <button
              type="submit"
              disabled={mutation.isPending}
              className="w-full px-4 py-2 bg-blue-600 text-white rounded-lg hover:bg-blue-700 disabled:opacity-50 disabled:cursor-not-allowed transition-colors"
            >
              {mutation.isPending ? 'Uploading...' : `Import ${selectedFiles.length} file${selectedFiles.length > 1 ? 's' : ''}`}
            </button>
          </div>
        )}

        {mutation.isError && (
          <div className="p-4 bg-red-50 dark:bg-red-900/20 border border-red-200 dark:border-red-800 rounded-lg">
            <p className="text-sm text-red-800 dark:text-red-200">
              Error: {mutation.error instanceof Error ? mutation.error.message : 'Unknown error'}
            </p>
          </div>
        )}
      </form>
    </div>
  );
}


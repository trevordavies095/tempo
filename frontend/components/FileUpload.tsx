'use client';

import { useMutation, useQueryClient } from '@tanstack/react-query';
import { useCallback, useState } from 'react';
import Link from 'next/link';
import { importWorkoutFile, type WorkoutImportResponse, type WorkoutImportSummaryResponse } from '@/lib/api';
import { useSettings } from '@/lib/settings';
import { invalidateWorkoutQueries } from '@/lib/queryUtils';
import { useFileDrop } from '@/hooks/useFileDrop';
import { formatDistance, formatDuration, formatPace, formatElevation } from '@/lib/format';
import { IconUpload, IconX } from '@tabler/icons-react';
import { Button } from '@/components/ui/Button';

export function FileUpload() {
  const [selectedFiles, setSelectedFiles] = useState<File[]>([]);
  const [importResult, setImportResult] = useState<WorkoutImportResponse | WorkoutImportSummaryResponse | null>(null);
  const { unitPreference } = useSettings();
  const queryClient = useQueryClient();

  const mutation = useMutation({
    mutationFn: (files: File[]) => importWorkoutFile(files, unitPreference),
    onSuccess: (data) => {
      invalidateWorkoutQueries(queryClient);
      setImportResult(data);
      setSelectedFiles([]);
    },
    onError: (error: Error) => {
      // Error is handled inline via mutation.isError
    },
  });

  const { dragActive, handleDrag, handleDrop, handleFileInput } = useFileDrop({
    onFilesSelected: (files) => {
      setSelectedFiles((prev) => [...prev, ...files]);
      setImportResult(null);
      // Only reset mutation state if not currently pending to prevent re-enabling submit button
      // during an in-flight upload, which could allow duplicate submissions
      if (!mutation.isPending) {
        mutation.reset(); // Clear mutation error state when new files are selected
      }
    },
    acceptExtensions: ['.gpx', '.fit', '.fit.gz'],
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
    <div className="w-full">
      <form onSubmit={handleSubmit} className="space-y-4">
        <div
          onDragEnter={handleDrag}
          onDragLeave={handleDrag}
          onDragOver={handleDrag}
          onDrop={handleDrop}
          className={`relative border-2 border-dashed rounded-tempo p-8 transition-colors ${
            dragActive
              ? 'border-volt bg-canvas'
              : 'border-border bg-canvas'
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
            <IconUpload className="mx-auto h-12 w-12 text-muted" />
            <p className="mt-2 text-sm text-muted">
              <span className="font-semibold text-ink">Click to upload</span> or drag and drop
            </p>
            <p className="text-xs text-muted">GPX or FIT files (multiple files supported)</p>
          </div>
        </div>

        {selectedFiles.length > 0 && (
          <div className="space-y-2">
            <div className="space-y-1 max-h-60 overflow-y-auto">
              {selectedFiles.map((file, index) => (
                <div
                  key={`${file.name}-${index}`}
                  className="flex items-center justify-between p-2 bg-raised rounded-tempo border border-border"
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
              {mutation.isPending ? 'Uploading...' : `Import ${selectedFiles.length} file${selectedFiles.length > 1 ? 's' : ''}`}
            </Button>
          </div>
        )}

        {mutation.isError && (
          <div className="p-4 bg-canvas border border-danger/40 rounded-tempo">
            <p className="text-sm text-danger">
              Error: {mutation.error instanceof Error ? mutation.error.message : 'Unknown error'}
            </p>
          </div>
        )}

        {importResult && (
          <div className="p-4 bg-raised border border-border rounded-tempo space-y-2">
            {('totalProcessed' in importResult) ? (
              // Multiple file response
              <>
                <h3 className="text-lg font-semibold text-ink">
                  Import Complete!
                </h3>
                <div className="text-sm text-ink space-y-1">
                  <p>
                    <span className="font-medium">Total processed:</span> {importResult.totalProcessed}
                  </p>
                  <p>
                    <span className="font-medium">Successfully imported:</span>{' '}
                    {importResult.successful}
                  </p>
                  {importResult.updated > 0 && (
                    <p>
                      <span className="font-medium">Updated:</span>{' '}
                      {importResult.updated}
                    </p>
                  )}
                  {importResult.skipped > 0 && (
                    <p>
                      <span className="font-medium">Skipped (duplicates):</span>{' '}
                      <span className="text-muted">{importResult.skipped}</span>
                    </p>
                  )}
                  {importResult.errors > 0 && (
                    <div>
                      <p>
                        <span className="font-medium">Errors:</span>{' '}
                        <span className="text-danger">{importResult.errors}</span>
                      </p>
                      {importResult.errorDetails.length > 0 && (
                        <details className="mt-2">
                          <summary className="cursor-pointer text-muted hover:text-ink hover:underline">
                            View error details
                          </summary>
                          <ul className="mt-2 ml-4 list-disc space-y-1">
                            {importResult.errorDetails.map((error, idx) => (
                              <li key={idx} className="text-xs">
                                <span className="font-mono">{error.filename}:</span> {error.error}
                              </li>
                            ))}
                          </ul>
                        </details>
                      )}
                    </div>
                  )}
                </div>
              </>
            ) : (
              // Single file response
              <>
                <h3 className="text-lg font-semibold text-ink">
                  Workout Imported Successfully!
                </h3>
                <div className="text-sm text-ink space-y-1">
                  <p>
                    <span className="font-medium">Distance:</span> {formatDistance(importResult.distanceM, unitPreference)}
                  </p>
                  <p>
                    <span className="font-medium">Duration:</span> {formatDuration(importResult.durationS)}
                  </p>
                  <p>
                    <span className="font-medium">Average Pace:</span> {formatPace(importResult.avgPaceS, unitPreference)}
                  </p>
                  {importResult.elevGainM !== null && importResult.elevGainM > 0 && (
                    <p>
                      <span className="font-medium">Elevation Gain:</span> {formatElevation(importResult.elevGainM, unitPreference)}
                    </p>
                  )}
                  <div className="pt-2">
                    <Link
                      href={`/dashboard/${importResult.id}`}
                      className="inline-flex items-center text-sm font-medium text-ink underline decoration-border hover:decoration-ink"
                    >
                      View workout details →
                    </Link>
                  </div>
                </div>
              </>
            )}
          </div>
        )}
      </form>
    </div>
  );
}

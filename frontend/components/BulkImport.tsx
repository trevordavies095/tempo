'use client';

import { useMutation, useQueryClient } from '@tanstack/react-query';
import { useCallback, useState } from 'react';
import { importBulkStravaExport, type BulkImportResponse } from '@/lib/api';
import { useSettings } from '@/lib/settings';

export function BulkImport() {
  const [dragActive, setDragActive] = useState(false);
  const [selectedFile, setSelectedFile] = useState<File | null>(null);
  const [importResult, setImportResult] = useState<BulkImportResponse | null>(null);
  const { unitPreference } = useSettings();
  const queryClient = useQueryClient();

  const mutation = useMutation({
    mutationFn: (file: File) => importBulkStravaExport(file, unitPreference),
    onSuccess: (data) => {
      // Invalidate all workout list queries (dashboard, activities page, home page)
      queryClient.invalidateQueries({ queryKey: ['workouts'] });
      // Invalidate stats queries
      queryClient.invalidateQueries({ queryKey: ['weeklyStats'] });
      queryClient.invalidateQueries({ queryKey: ['yearlyStats'] });
      queryClient.invalidateQueries({ queryKey: ['yearlyWeeklyStats'] });
      queryClient.invalidateQueries({ queryKey: ['availablePeriods'] });
      setImportResult(data);
      setSelectedFile(null);
    },
    onError: (error: Error) => {
      alert(`Error importing Strava export: ${error.message}`);
    },
  });

  const handleDrag = useCallback((e: React.DragEvent) => {
    e.preventDefault();
    e.stopPropagation();
    if (e.type === 'dragenter' || e.type === 'dragover') {
      setDragActive(true);
    } else if (e.type === 'dragleave') {
      setDragActive(false);
    }
  }, []);

  const handleDrop = useCallback(
    (e: React.DragEvent) => {
      e.preventDefault();
      e.stopPropagation();
      setDragActive(false);

      if (e.dataTransfer.files && e.dataTransfer.files[0]) {
        const file = e.dataTransfer.files[0];
        if (file.name.endsWith('.zip')) {
          setSelectedFile(file);
          setImportResult(null);
        } else {
          alert('Please upload a ZIP file containing your Strava export');
        }
      }
    },
    []
  );

  const handleFileInput = useCallback((e: React.ChangeEvent<HTMLInputElement>) => {
    if (e.target.files && e.target.files[0]) {
      const file = e.target.files[0];
      if (file.name.endsWith('.zip')) {
        setSelectedFile(file);
        setImportResult(null);
      } else {
        alert('Please upload a ZIP file containing your Strava export');
      }
    }
  }, []);

  const handleSubmit = useCallback(
    (e: React.FormEvent) => {
      e.preventDefault();
      if (selectedFile) {
        mutation.mutate(selectedFile);
      }
    },
    [selectedFile, mutation, unitPreference]
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
              ? 'border-green-500 bg-green-50 dark:bg-green-950'
              : 'border-gray-300 dark:border-gray-700 bg-gray-50 dark:bg-gray-900'
          }`}
        >
          <input
            type="file"
            id="bulk-upload"
            accept=".zip"
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
            <p className="text-xs text-gray-500 dark:text-gray-500">
              Strava export ZIP file (must contain activities.csv and activities/ folder)
            </p>
            {selectedFile && (
              <p className="mt-2 text-sm font-medium text-gray-900 dark:text-gray-100">
                Selected: {selectedFile.name}
              </p>
            )}
          </div>
        </div>

        {selectedFile && (
          <button
            type="submit"
            disabled={mutation.isPending}
            className="w-full px-4 py-2 bg-green-600 text-white rounded-lg hover:bg-green-700 disabled:opacity-50 disabled:cursor-not-allowed transition-colors"
          >
            {mutation.isPending ? 'Importing...' : 'Import Strava Export'}
          </button>
        )}

        {mutation.isError && (
          <div className="p-4 bg-red-50 dark:bg-red-900/20 border border-red-200 dark:border-red-800 rounded-lg">
            <p className="text-sm text-red-800 dark:text-red-200">
              Error: {mutation.error instanceof Error ? mutation.error.message : 'Unknown error'}
            </p>
          </div>
        )}

        {importResult && (
          <div className="p-4 bg-green-50 dark:bg-green-900/20 border border-green-200 dark:border-green-800 rounded-lg space-y-2">
            <h3 className="text-lg font-semibold text-green-900 dark:text-green-100">
              Import Complete!
            </h3>
            <div className="text-sm text-green-800 dark:text-green-200 space-y-1">
              <p>
                <span className="font-medium">Total processed:</span> {importResult.totalProcessed}
              </p>
              <p>
                <span className="font-medium">Successfully imported:</span>{' '}
                <span className="text-green-700 dark:text-green-300">{importResult.successful}</span>
              </p>
              {importResult.skipped > 0 && (
                <p>
                  <span className="font-medium">Skipped (duplicates):</span>{' '}
                  <span className="text-yellow-700 dark:text-yellow-300">{importResult.skipped}</span>
                </p>
              )}
              {importResult.errors > 0 && (
                <div>
                  <p>
                    <span className="font-medium">Errors:</span>{' '}
                    <span className="text-red-700 dark:text-red-300">{importResult.errors}</span>
                  </p>
                  {importResult.errorDetails.length > 0 && (
                    <details className="mt-2">
                      <summary className="cursor-pointer text-green-700 dark:text-green-300 hover:underline">
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
          </div>
        )}
      </form>
    </div>
  );
}


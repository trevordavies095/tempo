'use client';

import { useMutation, useQueryClient } from '@tanstack/react-query';
import { useCallback, useState } from 'react';
import { importTempoExport, type ExportImportResponse, getUnitPreference } from '@/lib/api';
import { invalidateWorkoutQueries } from '@/lib/queryUtils';
import { useFileDrop } from '@/hooks/useFileDrop';
import { useSettings } from '@/lib/settings';

export function TempoExportImport() {
  const [selectedFile, setSelectedFile] = useState<File | null>(null);
  const [importResult, setImportResult] = useState<ExportImportResponse | null>(null);
  const queryClient = useQueryClient();
  const { setUnitPreference } = useSettings();

  const { dragActive, handleDrag, handleDrop, handleFileInput } = useFileDrop({
    onFilesSelected: (files) => {
      if (files.length > 0) {
        setSelectedFile(files[0]);
        setImportResult(null);
      }
    },
    acceptExtensions: ['.zip'],
    maxFiles: 1,
  });

  const mutation = useMutation({
    mutationFn: (file: File) => importTempoExport(file),
    onSuccess: async (data) => {
      invalidateWorkoutQueries(queryClient);
      
      // Invalidate settings-related queries
      queryClient.invalidateQueries({ queryKey: ['heart-rate-zones'] });
      queryClient.invalidateQueries({ queryKey: ['default-shoe'] });
      
      // Refresh unit preference from backend
      try {
        const unitPref = await getUnitPreference();
        setUnitPreference(unitPref.unitPreference);
      } catch (error) {
        console.warn('Failed to refresh unit preference after import:', error);
      }
      
      setImportResult(data);
      setSelectedFile(null);
    },
    onError: (error: Error) => {
      alert(`Error importing Tempo export: ${error.message}`);
    },
  });

  const handleSubmit = useCallback(
    (e: React.FormEvent) => {
      e.preventDefault();
      if (selectedFile) {
        mutation.mutate(selectedFile);
      }
    },
    [selectedFile, mutation]
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
            id="tempo-export-upload"
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
              Tempo export ZIP file
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
            className="w-full px-4 py-2 bg-blue-600 text-white rounded-lg hover:bg-blue-700 disabled:opacity-50 disabled:cursor-not-allowed transition-colors"
          >
            {mutation.isPending ? 'Importing...' : 'Import Tempo Export'}
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
          <div className="p-4 bg-green-50 dark:bg-green-900/20 border border-green-200 dark:border-green-800 rounded-lg space-y-3">
            <h3 className="text-lg font-semibold text-green-900 dark:text-green-100">
              {importResult.success ? 'Import Complete!' : 'Import Completed with Errors'}
            </h3>
            <div className="text-sm text-green-800 dark:text-green-200 space-y-2">
              <div className="grid grid-cols-2 gap-2">
                <div>
                  <span className="font-medium">Settings:</span>{' '}
                  <span className="text-green-700 dark:text-green-300">
                    {importResult.statistics.settings.imported} imported
                  </span>
                  {importResult.statistics.settings.skipped > 0 && (
                    <span className="text-yellow-700 dark:text-yellow-300 ml-1">
                      ({importResult.statistics.settings.skipped} skipped)
                    </span>
                  )}
                </div>
                <div>
                  <span className="font-medium">Shoes:</span>{' '}
                  <span className="text-green-700 dark:text-green-300">
                    {importResult.statistics.shoes.imported} imported
                  </span>
                  {importResult.statistics.shoes.skipped > 0 && (
                    <span className="text-yellow-700 dark:text-yellow-300 ml-1">
                      ({importResult.statistics.shoes.skipped} skipped)
                    </span>
                  )}
                </div>
                <div>
                  <span className="font-medium">Workouts:</span>{' '}
                  <span className="text-green-700 dark:text-green-300">
                    {importResult.statistics.workouts.imported} imported
                  </span>
                  {importResult.statistics.workouts.skipped > 0 && (
                    <span className="text-yellow-700 dark:text-yellow-300 ml-1">
                      ({importResult.statistics.workouts.skipped} skipped)
                    </span>
                  )}
                </div>
                <div>
                  <span className="font-medium">Routes:</span>{' '}
                  <span className="text-green-700 dark:text-green-300">
                    {importResult.statistics.routes.imported} imported
                  </span>
                </div>
                <div>
                  <span className="font-medium">Splits:</span>{' '}
                  <span className="text-green-700 dark:text-green-300">
                    {importResult.statistics.splits.imported} imported
                  </span>
                </div>
                <div>
                  <span className="font-medium">Time Series:</span>{' '}
                  <span className="text-green-700 dark:text-green-300">
                    {importResult.statistics.timeSeries.imported} imported
                  </span>
                </div>
                <div>
                  <span className="font-medium">Media:</span>{' '}
                  <span className="text-green-700 dark:text-green-300">
                    {importResult.statistics.media.imported} imported
                  </span>
                  {importResult.statistics.media.skipped > 0 && (
                    <span className="text-yellow-700 dark:text-yellow-300 ml-1">
                      ({importResult.statistics.media.skipped} skipped)
                    </span>
                  )}
                </div>
                <div>
                  <span className="font-medium">Best Efforts:</span>{' '}
                  <span className="text-green-700 dark:text-green-300">
                    {importResult.statistics.bestEfforts.imported} imported
                  </span>
                  {importResult.statistics.bestEfforts.skipped > 0 && (
                    <span className="text-yellow-700 dark:text-yellow-300 ml-1">
                      ({importResult.statistics.bestEfforts.skipped} skipped)
                    </span>
                  )}
                </div>
              </div>
              
              {importResult.warnings && importResult.warnings.length > 0 && (
                <details className="mt-2">
                  <summary className="cursor-pointer text-yellow-700 dark:text-yellow-300 hover:underline">
                    View warnings ({importResult.warnings.length})
                  </summary>
                  <ul className="mt-2 ml-4 list-disc space-y-1">
                    {importResult.warnings.map((warning, idx) => (
                      <li key={idx} className="text-xs">
                        {warning}
                      </li>
                    ))}
                  </ul>
                </details>
              )}
              
              {importResult.errors && importResult.errors.length > 0 && (
                <details className="mt-2">
                  <summary className="cursor-pointer text-red-700 dark:text-red-300 hover:underline">
                    View errors ({importResult.errors.length})
                  </summary>
                  <ul className="mt-2 ml-4 list-disc space-y-1">
                    {importResult.errors.map((error, idx) => (
                      <li key={idx} className="text-xs">
                        {error}
                      </li>
                    ))}
                  </ul>
                </details>
              )}
            </div>
          </div>
        )}
      </form>
    </div>
  );
}


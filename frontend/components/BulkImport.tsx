'use client';

import { useCallback, useState } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import {
  importJobToBulkResponse,
  type BulkImportResponse,
  type ImportJob,
} from '@/lib/api';
import { useSettings } from '@/lib/settings';
import { invalidateWorkoutQueries } from '@/lib/queryUtils';
import { useFileDrop } from '@/hooks/useFileDrop';
import { useImportJobSession } from '@/hooks/useImportJobSession';
import { IconUpload } from '@tabler/icons-react';
import { Button } from '@/components/ui/Button';

export type BulkImportProps = {
  onJobCompleted?: (job: ImportJob) => void | Promise<void>;
  onJobFailedOrCancelled?: (message: string) => void;
};

export function BulkImport(props: BulkImportProps = {}) {
  const { onJobCompleted, onJobFailedOrCancelled } = props;
  const [selectedFile, setSelectedFile] = useState<File | null>(null);
  const [importResult, setImportResult] = useState<BulkImportResponse | null>(null);
  const { unitPreference } = useSettings();
  const queryClient = useQueryClient();

  const onCompleted = useCallback(
    async (job: ImportJob) => {
      invalidateWorkoutQueries(queryClient);
      setImportResult(importJobToBulkResponse(job));
      setSelectedFile(null);
      await onJobCompleted?.(job);
    },
    [queryClient, onJobCompleted]
  );

  const onFailed = useCallback(
    (message: string) => {
      onJobFailedOrCancelled?.(message);
    },
    [onJobFailedOrCancelled]
  );

  const session = useImportJobSession({
    kind: 'strava_bulk',
    unitPreference,
    onCompleted,
    onFailed,
  });

  const {
    canStart,
    isWorking,
    jobId,
    jobError,
    otherKindMessage,
    uploadError,
    cancelPending,
    progressLabel: getProgressLabel,
    startUpload,
    cancel,
    clearError,
  } = session;

  const { dragActive, handleDrag, handleDrop, handleFileInput } = useFileDrop({
    onFilesSelected: (files) => {
      if (files.length > 0) {
        setSelectedFile(files[0]);
        setImportResult(null);
        clearError();
      }
    },
    acceptExtensions: ['.zip'],
    maxFiles: 1,
  });

  const handleSubmit = useCallback(
    (e: React.FormEvent) => {
      e.preventDefault();
      if (selectedFile && canStart) {
        startUpload(selectedFile);
      }
    },
    [selectedFile, canStart, startUpload]
  );

  const progressLabel = getProgressLabel('Import Strava Export');
  const showError = !!jobError || !!uploadError;

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
            id="bulk-upload"
            accept=".zip"
            onChange={handleFileInput}
            disabled={isWorking}
            className="absolute inset-0 w-full h-full opacity-0 cursor-pointer"
          />
          <div className="text-center">
            <IconUpload className="mx-auto h-12 w-12 text-muted" />
            <p className="mt-2 text-sm text-muted">
              <span className="font-semibold text-ink">Click to upload</span> or drag and drop
            </p>
            <p className="text-xs text-muted">
              Strava export ZIP file (must contain activities.csv and activities/ folder)
            </p>
            {selectedFile && (
              <p className="mt-2 text-sm font-medium text-ink">
                Selected: {selectedFile.name}
              </p>
            )}
          </div>
        </div>

        {otherKindMessage && (
          <div className="p-4 bg-raised border border-border rounded-tempo">
            <p className="text-sm text-ink">{otherKindMessage}</p>
          </div>
        )}

        {selectedFile && (
          <Button
            type="submit"
            disabled={!canStart}
            className="w-full"
          >
            {progressLabel}
          </Button>
        )}

        {isWorking && !selectedFile && (
          <p className="text-sm text-ink">{progressLabel}</p>
        )}

        {isWorking && jobId && (
          <Button
            type="button"
            variant="secondary"
            disabled={cancelPending}
            className="w-full"
            onClick={() => cancel()}
          >
            Cancel import
          </Button>
        )}

        {showError && (
          <div className="p-4 bg-canvas border border-danger/40 rounded-tempo">
            <p className="text-sm text-danger">
              Error:{' '}
              {jobError ||
                (uploadError instanceof Error
                  ? uploadError.message
                  : 'Unknown error')}
            </p>
          </div>
        )}

        {importResult && (
          <div className="p-4 bg-raised border border-border rounded-tempo space-y-2">
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
                  <span className="font-medium">Updated with new data:</span>{' '}
                  {importResult.updated}
                </p>
              )}
              {importResult.skipped > 0 && (
                <p>
                  <span className="font-medium">Skipped (already complete):</span>{' '}
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
          </div>
        )}
      </form>
    </div>
  );
}

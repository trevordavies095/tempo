'use client';

import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useCallback, useEffect, useState } from 'react';
import {
  getImportJob,
  importBulkStravaExport,
  importJobToBulkResponse,
  type BulkImportResponse,
} from '@/lib/api';
import { useSettings } from '@/lib/settings';
import { invalidateWorkoutQueries } from '@/lib/queryUtils';
import { useFileDrop } from '@/hooks/useFileDrop';
import { IconUpload } from '@tabler/icons-react';
import { Button } from '@/components/ui/Button';

export function BulkImport() {
  const [selectedFile, setSelectedFile] = useState<File | null>(null);
  const [jobId, setJobId] = useState<string | null>(null);
  const [importResult, setImportResult] = useState<BulkImportResponse | null>(null);
  const [jobError, setJobError] = useState<string | null>(null);
  const { unitPreference } = useSettings();
  const queryClient = useQueryClient();

  const { dragActive, handleDrag, handleDrop, handleFileInput } = useFileDrop({
    onFilesSelected: (files) => {
      if (files.length > 0) {
        setSelectedFile(files[0]);
        setImportResult(null);
        setJobError(null);
      }
    },
    acceptExtensions: ['.zip'],
    maxFiles: 1,
  });

  const { data: job } = useQuery({
    queryKey: ['import-job', jobId],
    queryFn: () => getImportJob(jobId!),
    enabled: !!jobId,
    refetchInterval: (query) => {
      const status = query.state.data?.status;
      if (status === 'completed' || status === 'failed') {
        return false;
      }
      return 1000;
    },
  });

  useEffect(() => {
    if (!job) {
      return;
    }

    if (job.status === 'completed') {
      invalidateWorkoutQueries(queryClient);
      setImportResult(importJobToBulkResponse(job));
      setSelectedFile(null);
      setJobId(null);
      setJobError(null);
    } else if (job.status === 'failed') {
      setJobError(job.errorMessage || 'Import failed');
      setJobId(null);
    }
  }, [job, queryClient]);

  const mutation = useMutation({
    mutationFn: (file: File) => importBulkStravaExport(file, unitPreference),
    onSuccess: (started) => {
      setImportResult(null);
      setJobError(null);
      setJobId(started.id);
    },
    onError: (error: Error) => {
      setJobError(error.message);
    },
  });

  const isWorking = mutation.isPending || !!jobId;

  const handleSubmit = useCallback(
    (e: React.FormEvent) => {
      e.preventDefault();
      if (selectedFile) {
        mutation.mutate(selectedFile);
      }
    },
    [selectedFile, mutation]
  );

  const progressLabel = (() => {
    if (mutation.isPending) {
      return 'Uploading...';
    }
    if (job && job.total > 0) {
      return `Importing ${job.processed}/${job.total}...`;
    }
    if (jobId) {
      return 'Importing...';
    }
    return 'Import Strava Export';
  })();

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

        {selectedFile && (
          <Button
            type="submit"
            disabled={isWorking}
            className="w-full"
          >
            {progressLabel}
          </Button>
        )}

        {(mutation.isError || jobError) && (
          <div className="p-4 bg-canvas border border-danger/40 rounded-tempo">
            <p className="text-sm text-danger">
              Error: {jobError || (mutation.error instanceof Error ? mutation.error.message : 'Unknown error')}
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

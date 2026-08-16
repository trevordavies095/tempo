'use client';

import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useCallback, useEffect, useState } from 'react';
import {
  cancelImportJob,
  getCurrentImportJob,
  getImportJob,
  IMPORT_JOB_HINT_KEY,
  importJobToBulkResponse,
  importStravaExportChunked,
  ImportJobConflictError,
  type BulkImportResponse,
  type ImportJob,
} from '@/lib/api';
import { useSettings } from '@/lib/settings';
import { invalidateWorkoutQueries } from '@/lib/queryUtils';
import { useFileDrop } from '@/hooks/useFileDrop';
import { IconUpload } from '@tabler/icons-react';
import { Button } from '@/components/ui/Button';

function persistJobHint(id: string | null) {
  if (typeof window === 'undefined') {
    return;
  }
  if (id) {
    sessionStorage.setItem(IMPORT_JOB_HINT_KEY, id);
  } else {
    sessionStorage.removeItem(IMPORT_JOB_HINT_KEY);
  }
}

export function BulkImport() {
  const [selectedFile, setSelectedFile] = useState<File | null>(null);
  const [jobId, setJobId] = useState<string | null>(null);
  const [importResult, setImportResult] = useState<BulkImportResponse | null>(null);
  const [jobError, setJobError] = useState<string | null>(null);
  const [uploadBytes, setUploadBytes] = useState<{ received: number; size: number } | null>(null);
  const { unitPreference } = useSettings();
  const queryClient = useQueryClient();

  const attachJob = useCallback((id: string) => {
    persistJobHint(id);
    setJobId(id);
    setImportResult(null);
    setJobError(null);
  }, []);

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

  useEffect(() => {
    let cancelled = false;
    (async () => {
      try {
        const current = await getCurrentImportJob();
        if (cancelled) {
          return;
        }
        if (current) {
          attachJob(current.id);
          return;
        }

        const hint = sessionStorage.getItem(IMPORT_JOB_HINT_KEY);
        if (!hint) {
          return;
        }
        const hinted = await getImportJob(hint);
        if (cancelled) {
          return;
        }
        if (hinted.status === 'completed') {
          setImportResult(importJobToBulkResponse(hinted));
          persistJobHint(null);
        } else if (hinted.status === 'failed') {
          setJobError(hinted.errorMessage || 'Import failed');
          persistJobHint(null);
        } else {
          attachJob(hinted.id);
        }
      } catch {
        persistJobHint(null);
      }
    })();
    return () => {
      cancelled = true;
    };
  }, [attachJob]);

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
      setUploadBytes(null);
      persistJobHint(null);
    } else if (job.status === 'failed') {
      setJobError(job.errorMessage || 'Import failed');
      setJobId(null);
      setUploadBytes(null);
      persistJobHint(null);
    }
  }, [job, queryClient]);

  const mutation = useMutation({
    mutationFn: (file: File) =>
      importStravaExportChunked(
        file,
        unitPreference,
        (received, size) => {
          setUploadBytes({ received, size });
        },
        (created) => {
          attachJob(created.id);
        }
      ),
    onSuccess: (started) => {
      setUploadBytes(null);
      attachJob(started.id);
    },
    onError: (error: Error) => {
      if (error instanceof ImportJobConflictError) {
        attachJob(error.job.id);
        return;
      }
      setJobError(error.message);
    },
  });

  const cancelMutation = useMutation({
    mutationFn: (id: string) => cancelImportJob(id),
    onSuccess: (cancelled: ImportJob) => {
      setJobError(cancelled.errorMessage || 'cancelled');
      setJobId(null);
      setUploadBytes(null);
      persistJobHint(null);
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
    if (mutation.isPending && uploadBytes && uploadBytes.size > 0) {
      const pct = Math.min(100, Math.round((uploadBytes.received / uploadBytes.size) * 100));
      return `Uploading ${pct}%...`;
    }
    if (mutation.isPending) {
      return 'Uploading...';
    }
    if (job?.status === 'receiving' && job.byteSize > 0) {
      const pct = Math.min(100, Math.round((job.bytesReceived / job.byteSize) * 100));
      return `Uploading ${pct}%...`;
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

        {isWorking && !selectedFile && (
          <p className="text-sm text-ink">{progressLabel}</p>
        )}

        {isWorking && jobId && (
          <Button
            type="button"
            variant="secondary"
            disabled={cancelMutation.isPending}
            className="w-full"
            onClick={() => cancelMutation.mutate(jobId)}
          >
            Cancel import
          </Button>
        )}

        {(mutation.isError || jobError) && !(mutation.error instanceof ImportJobConflictError) && (
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

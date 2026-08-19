'use client';

import { useCallback, useState } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import {
  getUnitPreference,
  importJobToExportImportResponse,
  type ExportImportResponse,
  type ImportJob,
} from '@/lib/api';
import { invalidateWorkoutQueries } from '@/lib/queryUtils';
import { useFileDrop } from '@/hooks/useFileDrop';
import { useImportJobSession } from '@/hooks/useImportJobSession';
import { useSettings } from '@/lib/settings';
import { IconUpload } from '@tabler/icons-react';
import { Button } from '@/components/ui/Button';

export type TempoExportImportProps = {
  onJobCompleted?: (job: ImportJob) => void | Promise<void>;
  onJobFailedOrCancelled?: (message: string) => void;
};

export function TempoExportImport(props: TempoExportImportProps = {}) {
  const { onJobCompleted, onJobFailedOrCancelled } = props;
  const [selectedFile, setSelectedFile] = useState<File | null>(null);
  const [importResult, setImportResult] = useState<ExportImportResponse | null>(null);
  const queryClient = useQueryClient();
  const { setUnitPreference } = useSettings();

  const onCompleted = useCallback(
    async (job: ImportJob) => {
      invalidateWorkoutQueries(queryClient);
      queryClient.invalidateQueries({ queryKey: ['heart-rate-zones'] });
      queryClient.invalidateQueries({ queryKey: ['default-shoe'] });
      try {
        const unitPref = await getUnitPreference();
        setUnitPreference(unitPref.unitPreference);
      } catch (error) {
        console.warn('Failed to refresh unit preference after import:', error);
      }
      setImportResult(importJobToExportImportResponse(job));
      setSelectedFile(null);
      await onJobCompleted?.(job);
    },
    [queryClient, setUnitPreference, onJobCompleted]
  );

  const onFailed = useCallback(
    (message: string) => {
      onJobFailedOrCancelled?.(message);
    },
    [onJobFailedOrCancelled]
  );

  const session = useImportJobSession({
    kind: 'tempo_export',
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

  const progressLabel = getProgressLabel('Import Tempo Export');
  const showError = !!jobError || !!uploadError;

  return (
    <div className="w-full max-w-2xl">
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
            id="tempo-export-upload"
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
              Tempo export ZIP file
            </p>
            {selectedFile && (
              <p className="mt-2 text-sm font-medium text-ink">
                Selected: {selectedFile.name}
              </p>
            )}
          </div>
        </div>

        {otherKindMessage && (
          <div className="p-4 bg-canvas border border-border rounded-tempo">
            <p className="text-sm text-ink">{otherKindMessage}</p>
          </div>
        )}

        {selectedFile && (
          <Button
            type="submit"
            disabled={!canStart}
            className="w-full"
          >
            {isWorking ? progressLabel : 'Import Tempo Export'}
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
          <div className="p-4 bg-canvas border border-danger rounded-tempo">
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
          <div className="p-4 bg-canvas border border-border rounded-tempo space-y-3">
            <h3 className="text-lg font-semibold text-ink">
              {importResult.success ? 'Import Complete!' : 'Import Completed with Errors'}
            </h3>
            <div className="text-sm text-ink space-y-2">
              <div className="grid grid-cols-2 gap-2">
                <div>
                  <span className="font-medium">Settings:</span>{' '}
                  <span className="text-muted">
                    {importResult.statistics.settings.imported} imported
                  </span>
                  {importResult.statistics.settings.skipped > 0 && (
                    <span className="text-muted ml-1">
                      ({importResult.statistics.settings.skipped} skipped)
                    </span>
                  )}
                </div>
                <div>
                  <span className="font-medium">Shoes:</span>{' '}
                  <span className="text-muted">
                    {importResult.statistics.shoes.imported} imported
                  </span>
                  {importResult.statistics.shoes.skipped > 0 && (
                    <span className="text-muted ml-1">
                      ({importResult.statistics.shoes.skipped} skipped)
                    </span>
                  )}
                </div>
                <div>
                  <span className="font-medium">Workouts:</span>{' '}
                  <span className="text-muted">
                    {importResult.statistics.workouts.imported} imported
                  </span>
                  {importResult.statistics.workouts.skipped > 0 && (
                    <span className="text-muted ml-1">
                      ({importResult.statistics.workouts.skipped} skipped)
                    </span>
                  )}
                </div>
                <div>
                  <span className="font-medium">Routes:</span>{' '}
                  <span className="text-muted">
                    {importResult.statistics.routes.imported} imported
                  </span>
                </div>
                <div>
                  <span className="font-medium">Splits:</span>{' '}
                  <span className="text-muted">
                    {importResult.statistics.splits.imported} imported
                  </span>
                </div>
                <div>
                  <span className="font-medium">Time Series:</span>{' '}
                  <span className="text-muted">
                    {importResult.statistics.timeSeries.imported} imported
                  </span>
                </div>
                <div>
                  <span className="font-medium">Media:</span>{' '}
                  <span className="text-muted">
                    {importResult.statistics.media.imported} imported
                  </span>
                  {importResult.statistics.media.skipped > 0 && (
                    <span className="text-muted ml-1">
                      ({importResult.statistics.media.skipped} skipped)
                    </span>
                  )}
                </div>
                <div>
                  <span className="font-medium">Best Efforts:</span>{' '}
                  <span className="text-muted">
                    {importResult.statistics.bestEfforts.imported} imported
                  </span>
                  {importResult.statistics.bestEfforts.skipped > 0 && (
                    <span className="text-muted ml-1">
                      ({importResult.statistics.bestEfforts.skipped} skipped)
                    </span>
                  )}
                </div>
              </div>

              {importResult.warnings && importResult.warnings.length > 0 && (
                <details className="mt-2">
                  <summary className="cursor-pointer text-muted hover:underline">
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
                  <summary className="cursor-pointer text-danger hover:underline">
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

'use client';

import { useMutation, useQuery } from '@tanstack/react-query';
import { useCallback, useEffect, useRef, useState } from 'react';
import {
  cancelImportJob,
  getCurrentImportJob,
  getImportJob,
  IMPORT_JOB_HINT_KEY,
  ImportJobConflictError,
  importStravaExportChunked,
  importTempoExportChunked,
  type ImportJob,
  type ImportJobKind,
} from '@/lib/api';

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

function otherKindBanner(kind: ImportJobKind): string {
  if (kind === 'tempo_export') {
    return 'A Strava import is already running on the Import page. Finish or cancel it before restoring a Tempo export.';
  }
  return 'A Tempo export restore is already running in Settings. Finish or cancel it before starting a Strava import.';
}

export type UseImportJobSessionOptions = {
  kind: ImportJobKind;
  unitPreference?: 'metric' | 'imperial';
  onCompleted: (job: ImportJob) => void | Promise<void>;
};

export function useImportJobSession({ kind, unitPreference, onCompleted }: UseImportJobSessionOptions) {
  const [jobId, setJobId] = useState<string | null>(null);
  const [uploadBytes, setUploadBytes] = useState<{ received: number; size: number } | null>(null);
  const [jobError, setJobError] = useState<string | null>(null);
  const [otherKindMessage, setOtherKindMessage] = useState<string | null>(null);
  const handledTerminalIdRef = useRef<string | null>(null);

  const attachJob = useCallback((id: string) => {
    handledTerminalIdRef.current = null;
    persistJobHint(id);
    setJobId(id);
    setJobError(null);
    setOtherKindMessage(null);
  }, []);

  const clearWorkingState = useCallback(() => {
    setJobId(null);
    setUploadBytes(null);
    persistJobHint(null);
  }, []);

  useEffect(() => {
    let cancelled = false;
    (async () => {
      try {
        const current = await getCurrentImportJob();
        if (cancelled) {
          return;
        }
        if (current) {
          if (current.kind === kind) {
            attachJob(current.id);
          } else {
            setOtherKindMessage(otherKindBanner(kind));
          }
          return;
        }

        setOtherKindMessage(null);
        const hint = sessionStorage.getItem(IMPORT_JOB_HINT_KEY);
        if (!hint) {
          return;
        }
        const hinted = await getImportJob(hint);
        if (cancelled) {
          return;
        }
        if (hinted.kind !== kind) {
          persistJobHint(null);
          return;
        }
        if (hinted.status === 'completed') {
          await onCompleted(hinted);
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
  }, [attachJob, kind, onCompleted]);

  const { data: job } = useQuery({
    queryKey: ['import-job', kind, jobId],
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

  // Poll completion is an external system update; apply terminal UI state on the next tick.
  useEffect(() => {
    if (!job || (job.status !== 'completed' && job.status !== 'failed')) {
      return;
    }
    if (handledTerminalIdRef.current === job.id) {
      return;
    }
    handledTerminalIdRef.current = job.id;
    const terminal = job;
    const timer = window.setTimeout(() => {
      if (terminal.status === 'completed') {
        clearWorkingState();
        setJobError(null);
        void onCompleted(terminal);
      } else {
        setJobError(terminal.errorMessage || 'Import failed');
        clearWorkingState();
      }
    }, 0);
    return () => window.clearTimeout(timer);
  }, [job, onCompleted, clearWorkingState]);

  const uploadMutation = useMutation({
    mutationFn: (file: File) => {
      if (kind === 'tempo_export') {
        return importTempoExportChunked(
          file,
          (received, size) => setUploadBytes({ received, size }),
          (created) => attachJob(created.id)
        );
      }
      return importStravaExportChunked(
        file,
        unitPreference,
        (received, size) => setUploadBytes({ received, size }),
        (created) => attachJob(created.id)
      );
    },
    onSuccess: (started) => {
      setUploadBytes(null);
      attachJob(started.id);
    },
    onError: (error: Error) => {
      if (error instanceof ImportJobConflictError) {
        if (error.job.kind === kind) {
          attachJob(error.job.id);
        } else {
          setOtherKindMessage(otherKindBanner(kind));
        }
        return;
      }
      setJobError(error.message);
    },
  });

  const cancelMutation = useMutation({
    mutationFn: (id: string) => cancelImportJob(id),
    onSuccess: (cancelled: ImportJob) => {
      setJobError(cancelled.errorMessage || 'cancelled');
      clearWorkingState();
    },
    onError: (error: Error) => {
      setJobError(error.message);
    },
  });

  const isWorking = uploadMutation.isPending || !!jobId;
  const canStart = !isWorking && !otherKindMessage;

  const clearError = useCallback(() => setJobError(null), []);

  const startUpload = useCallback(
    (file: File) => {
      setJobError(null);
      uploadMutation.mutate(file);
    },
    [uploadMutation]
  );

  const cancel = useCallback(() => {
    if (jobId) {
      cancelMutation.mutate(jobId);
    }
  }, [jobId, cancelMutation]);

  const progressLabel = useCallback(
    (idleLabel: string) => {
      if (uploadMutation.isPending && uploadBytes && uploadBytes.size > 0) {
        const pct = Math.min(100, Math.round((uploadBytes.received / uploadBytes.size) * 100));
        return `Uploading ${pct}%...`;
      }
      if (uploadMutation.isPending) {
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
      return idleLabel;
    },
    [uploadMutation.isPending, uploadBytes, job, jobId]
  );

  return {
    job,
    jobId,
    jobError,
    otherKindMessage,
    isWorking,
    canStart,
    uploadPending: uploadMutation.isPending,
    uploadError: uploadMutation.isError && !(uploadMutation.error instanceof ImportJobConflictError)
      ? uploadMutation.error
      : null,
    cancelPending: cancelMutation.isPending,
    progressLabel,
    startUpload,
    cancel,
    clearError,
  };
}

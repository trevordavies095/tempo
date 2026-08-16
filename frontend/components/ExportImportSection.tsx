'use client';

import { useState } from 'react';
import { TempoExportImport } from './TempoExportImport';
import { BulkImport } from './BulkImport';
import { exportAllData } from '@/lib/api';
import { Button } from '@/components/ui/Button';
import { Card } from '@/components/ui/Card';

export function ExportImportSection() {
  const [isExporting, setIsExporting] = useState(false);
  const [exportError, setExportError] = useState<string | null>(null);
  const [exportSuccess, setExportSuccess] = useState(false);

  const handleExport = async () => {
    setIsExporting(true);
    setExportError(null);
    setExportSuccess(false);

    try {
      const blob = await exportAllData();
      
      const timestamp = new Date().toISOString().replace(/[:.]/g, '-').slice(0, -5);
      const filename = `tempo-export-${timestamp}.zip`;
      
      const url = window.URL.createObjectURL(blob);
      const a = document.createElement('a');
      a.href = url;
      a.download = filename;
      
      document.body.appendChild(a);
      a.click();
      
      window.URL.revokeObjectURL(url);
      document.body.removeChild(a);
      
      setExportSuccess(true);
      setTimeout(() => setExportSuccess(false), 3000);
    } catch (error) {
      setExportError(error instanceof Error ? error.message : 'Failed to export data');
    } finally {
      setIsExporting(false);
    }
  };

  return (
    <Card>
      <h2 className="text-lg font-semibold text-ink mb-4">Export</h2>
      <p className="text-sm text-muted mb-6">
        Download a complete backup of all your Tempo data as a ZIP file. The export includes workouts,
        media, shoes, settings, and calculated data. Use it for backups or migrating to a new instance.
      </p>

      <div className="space-y-4">
        <div>
          <h3 className="text-md font-medium text-ink mb-2">Export Data</h3>
          <p className="text-sm text-muted mb-4">
            Download a complete backup of all your Tempo data as a ZIP file.
          </p>
          <div className="flex flex-col gap-2">
            <Button
              onClick={handleExport}
              disabled={isExporting}
              className="w-fit"
            >
              {isExporting ? 'Exporting...' : 'Export All Data'}
            </Button>
            {exportSuccess && (
              <span className="text-sm text-ink">
                Export completed successfully! Your download should start shortly.
              </span>
            )}
            {exportError && (
              <span className="text-sm text-danger">
                {exportError}
              </span>
            )}
          </div>
        </div>

        <details className="pt-4 border-t border-border group">
          <summary className="cursor-pointer list-none flex items-center justify-between gap-2">
            <div>
              <h3 className="text-md font-medium text-ink">Migrate / restore</h3>
              <p className="text-sm text-muted mt-1">
                Restore a Tempo export or import a Strava archive after onboarding. Expand to upload a ZIP.
              </p>
            </div>
            <span className="text-muted text-sm shrink-0 group-open:hidden">Show</span>
            <span className="text-muted text-sm shrink-0 hidden group-open:inline">Hide</span>
          </summary>

          <div className="mt-4 space-y-8">
            <div>
              <h4 className="text-sm font-medium text-ink mb-2">Restore Tempo export</h4>
              <p className="text-sm text-muted mb-4">
                Import a previously exported Tempo backup. Workouts, media, shoes, settings, and
                calculated data are restored; duplicates are skipped automatically.
              </p>
              <TempoExportImport />
            </div>

            <div className="pt-4 border-t border-border">
              <h4 className="text-sm font-medium text-ink mb-2">Import Strava archive</h4>
              <p className="text-sm text-muted mb-4">
                Upload a Strava data export ZIP that includes{' '}
                <code className="px-1 py-0.5 bg-canvas border border-border rounded-tempo text-ink">
                  activities.csv
                </code>{' '}
                and an{' '}
                <code className="px-1 py-0.5 bg-canvas border border-border rounded-tempo text-ink">
                  activities/
                </code>{' '}
                folder with GPX or FIT files.
              </p>
              <BulkImport />
            </div>
          </div>
        </details>
      </div>
    </Card>
  );
}

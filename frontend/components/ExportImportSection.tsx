'use client';

import { useState } from 'react';
import { TempoExportImport } from './TempoExportImport';
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
      <h2 className="text-lg font-semibold text-ink mb-4">Export / Import</h2>
      <p className="text-sm text-muted mb-6">
        Export all your Tempo data including workouts, media files, shoes, settings, and best efforts in a portable ZIP format. 
        You can use this export to back up your data or migrate to a new instance.
      </p>

      <div className="space-y-4">
        <div>
          <h3 className="text-md font-medium text-ink mb-2">Export Data</h3>
          <p className="text-sm text-muted mb-4">
            Download a complete backup of all your Tempo data as a ZIP file. The export includes all workouts, 
            media files, shoes, settings, and calculated data.
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

        <div className="pt-4 border-t border-border">
          <h3 className="text-md font-medium text-ink mb-2">Import Data</h3>
          <p className="text-sm text-muted mb-4">
            Import a previously exported Tempo backup file to restore your data. This will restore all workouts, 
            media files, shoes, settings, and calculated data. Duplicates will be skipped automatically.
          </p>
          <TempoExportImport />
        </div>
      </div>
    </Card>
  );
}

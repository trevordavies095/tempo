'use client';

import { useState } from 'react';
import { TempoExportImport } from './TempoExportImport';
import { exportAllData } from '@/lib/api';

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
      
      // Generate filename with timestamp
      const timestamp = new Date().toISOString().replace(/[:.]/g, '-').slice(0, -5);
      const filename = `tempo-export-${timestamp}.zip`;
      
      // Create a download link
      const url = window.URL.createObjectURL(blob);
      const a = document.createElement('a');
      a.href = url;
      a.download = filename;
      
      document.body.appendChild(a);
      a.click();
      
      // Cleanup
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
    <div className="bg-white dark:bg-gray-900 p-6 rounded-lg border border-gray-200 dark:border-gray-800">
      <h2 className="text-xl font-semibold text-gray-900 dark:text-gray-100 mb-4">
        Export / Import
      </h2>
      <p className="text-sm text-gray-600 dark:text-gray-400 mb-6">
        Export all your Tempo data including workouts, media files, shoes, settings, and best efforts in a portable ZIP format. 
        You can use this export to back up your data or migrate to a new instance.
      </p>

      <div className="space-y-4">
        {/* Export Section */}
        <div>
          <h3 className="text-md font-medium text-gray-900 dark:text-gray-100 mb-2">
            Export Data
          </h3>
          <p className="text-sm text-gray-600 dark:text-gray-400 mb-4">
            Download a complete backup of all your Tempo data as a ZIP file. The export includes all workouts, 
            media files, shoes, settings, and calculated data.
          </p>
          <div className="flex flex-col gap-2">
            <button
              onClick={handleExport}
              disabled={isExporting}
              className={`px-6 py-3 rounded-lg font-medium transition-colors w-fit ${
                isExporting
                  ? 'bg-gray-400 text-white cursor-not-allowed'
                  : 'bg-blue-600 text-white hover:bg-blue-700 dark:bg-blue-500 dark:hover:bg-blue-600'
              }`}
            >
              {isExporting ? 'Exporting...' : 'Export All Data'}
            </button>
            {exportSuccess && (
              <span className="text-sm text-green-600 dark:text-green-400">
                Export completed successfully! Your download should start shortly.
              </span>
            )}
            {exportError && (
              <span className="text-sm text-red-600 dark:text-red-400">
                {exportError}
              </span>
            )}
          </div>
        </div>

        {/* Import Section */}
        <div className="pt-4 border-t border-gray-200 dark:border-gray-800">
          <h3 className="text-md font-medium text-gray-900 dark:text-gray-100 mb-2">
            Import Data
          </h3>
          <p className="text-sm text-gray-600 dark:text-gray-400 mb-4">
            Import a previously exported Tempo backup file to restore your data. This will restore all workouts, 
            media files, shoes, settings, and calculated data. Duplicates will be skipped automatically.
          </p>
          <TempoExportImport />
        </div>
      </div>
    </div>
  );
}


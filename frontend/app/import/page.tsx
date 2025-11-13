import Link from 'next/link';
import { FileUpload } from '@/components/FileUpload';
import { BulkImport } from '@/components/BulkImport';

export default function ImportPage() {
  return (
    <div className="flex min-h-screen items-center justify-center bg-zinc-50 dark:bg-black">
      <main className="flex min-h-screen w-full max-w-4xl flex-col items-center justify-start py-16 px-8">
        <div className="w-full mb-8">
          <div className="flex items-center justify-between">
            <div>
              <h1 className="text-4xl font-bold text-gray-900 dark:text-gray-100 mb-2">
                Tempo
              </h1>
              <p className="text-lg text-gray-600 dark:text-gray-400">
                Self-hostable running tracker
              </p>
            </div>
            <div className="flex gap-4">
              <Link
                href="/settings"
                className="px-4 py-2 text-sm font-medium text-gray-700 dark:text-gray-300 hover:text-gray-900 dark:hover:text-gray-100"
              >
                Settings
              </Link>
              <Link
                href="/dashboard"
                className="px-4 py-2 text-sm font-medium text-gray-700 dark:text-gray-300 hover:text-gray-900 dark:hover:text-gray-100"
              >
                Dashboard
              </Link>
            </div>
          </div>
        </div>

        <div className="w-full space-y-8">
          <div>
            <h2 className="text-2xl font-semibold text-gray-900 dark:text-gray-100 mb-4">
              Import Single GPX Workout
            </h2>
            <FileUpload />
          </div>

          <div className="border-t border-gray-200 dark:border-gray-800 pt-8">
            <h2 className="text-2xl font-semibold text-gray-900 dark:text-gray-100 mb-4">
              Bulk Import Strava Export
            </h2>
            <p className="text-sm text-gray-600 dark:text-gray-400 mb-4">
              Upload a ZIP file containing your Strava data export. The ZIP should include{' '}
              <code className="px-1 py-0.5 bg-gray-200 dark:bg-gray-800 rounded">activities.csv</code>{' '}
              and an <code className="px-1 py-0.5 bg-gray-200 dark:bg-gray-800 rounded">activities/</code>{' '}
              folder with GPX or FIT files.
            </p>
            <BulkImport />
          </div>
        </div>
      </main>
    </div>
  );
}


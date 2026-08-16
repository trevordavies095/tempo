'use client';

import { FileUpload } from '@/components/FileUpload';
import { BulkImport } from '@/components/BulkImport';
import { AuthGuard } from '@/components/AuthGuard';
import { PageShell } from '@/components/ui/PageShell';
import { Card } from '@/components/ui/Card';

function ImportPageContent() {
  return (
    <PageShell
      density="control"
      title="Import Workouts"
      subtitle="Upload GPX files or bulk import from Strava"
    >
      <div className="w-full space-y-8">
        <Card>
          <h2 className="text-lg font-semibold text-ink mb-4">
            Import Single Workout
          </h2>
          <FileUpload />
        </Card>

        <Card>
          <h2 className="text-lg font-semibold text-ink mb-2">
            Bulk Import Strava Export
          </h2>
          <p className="text-sm text-muted mb-4">
            Upload a ZIP file containing your Strava data export. The ZIP should include{' '}
            <code className="px-1 py-0.5 bg-canvas border border-border rounded-tempo text-ink">activities.csv</code>{' '}
            and an <code className="px-1 py-0.5 bg-canvas border border-border rounded-tempo text-ink">activities/</code>{' '}
            folder with GPX or FIT files.
          </p>
          <BulkImport />
        </Card>
      </div>
    </PageShell>
  );
}

export default function ImportPage() {
  return (
    <AuthGuard>
      <ImportPageContent />
    </AuthGuard>
  );
}

'use client';

import { FileUpload } from '@/components/FileUpload';
import { AuthGuard } from '@/components/AuthGuard';
import { PageShell } from '@/components/ui/PageShell';
import { Card } from '@/components/ui/Card';

function ImportPageContent() {
  return (
    <PageShell
      density="control"
      title="Import Workouts"
      subtitle="Upload GPX or FIT files"
    >
      <div className="w-full space-y-8">
        <Card>
          <h2 className="text-lg font-semibold text-ink mb-4">
            Import workouts
          </h2>
          <FileUpload />
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

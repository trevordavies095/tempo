'use client';

import { PageShell } from '@/components/ui/PageShell';
import { Card } from '@/components/ui/Card';

export default function CoachHistoryPage() {
  return (
    <PageShell density="control" title="History" subtitle="Committed weeks and privacy receipts">
      <Card>
        <p className="text-sm text-muted">
          History of your coached weeks will live here. Coming soon.
        </p>
      </Card>
    </PageShell>
  );
}

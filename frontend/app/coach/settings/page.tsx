'use client';

import { PageShell } from '@/components/ui/PageShell';
import { Card } from '@/components/ui/Card';

export default function CoachSettingsPage() {
  return (
    <PageShell
      density="control"
      title="Settings"
      subtitle="Profile, fixed dates, and what your coach knows about you"
    >
      <Card>
        <p className="text-sm text-muted">
          Coach settings will live here. Coming soon — use global Settings for your Anthropic key
          for now.
        </p>
      </Card>
    </PageShell>
  );
}

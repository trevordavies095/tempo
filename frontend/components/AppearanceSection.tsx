'use client';

import { useAppearance } from '@/contexts/AppearanceContext';
import type { AppearancePreference } from '@/lib/appearance';
import { Button } from '@/components/ui/Button';
import { Card } from '@/components/ui/Card';

const OPTIONS: { value: AppearancePreference; label: string }[] = [
  { value: 'system', label: 'System' },
  { value: 'dark', label: 'Dark' },
  { value: 'light', label: 'Light' },
];

export default function AppearanceSection() {
  const { preference, setPreference } = useAppearance();

  return (
    <Card>
      <h2 className="text-lg font-semibold text-ink mb-4">Appearance</h2>
      <p className="text-sm text-muted mb-4">
        Dark-first command center. System follows your operating system. Stored
        on this browser only.
      </p>

      <div className="flex flex-wrap gap-3">
        {OPTIONS.map(({ value, label }) => (
          <Button
            key={value}
            type="button"
            variant={preference === value ? 'primary' : 'secondary'}
            onClick={() => setPreference(value)}
          >
            {label}
          </Button>
        ))}
      </div>
    </Card>
  );
}

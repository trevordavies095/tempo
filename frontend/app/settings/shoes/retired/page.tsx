'use client';

import Link from 'next/link';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { getShoes, updateShoe } from '@/lib/api';
import { AuthGuard } from '@/components/AuthGuard';
import { PageShell } from '@/components/ui/PageShell';
import { Card } from '@/components/ui/Card';
import { Button } from '@/components/ui/Button';
import { EmptyState } from '@/components/ui/EmptyState';

function RetiredShoesContent() {
  const queryClient = useQueryClient();
  const { data: shoes, isLoading } = useQuery({
    queryKey: ['shoes', 'retired'],
    queryFn: () => getShoes({ status: 'retired' }),
  });

  const unretireMutation = useMutation({
    mutationFn: (id: string) => updateShoe(id, { isRetired: false }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['shoes'] });
      queryClient.invalidateQueries({ queryKey: ['default-shoe'] });
    },
  });

  return (
    <PageShell
      density="control"
      title="Retired shoes"
      subtitle="Shoes you retired stay here with their mileage. Un-retire to use them in lists again."
      leading={
        <Link href="/settings" className="text-sm text-muted hover:text-ink">
          ← Back to Settings
        </Link>
      }
    >
      <Card>
        {isLoading ? (
          <p className="text-muted">Loading…</p>
        ) : !shoes?.length ? (
          <EmptyState
            title="No retired shoes"
            description="Retired pairs will show up here with their mileage."
          />
        ) : (
          <ul className="space-y-4">
            {shoes.map((shoe) => (
              <li
                key={shoe.id}
                className="flex flex-col sm:flex-row sm:items-center sm:justify-between gap-3 p-4 bg-canvas rounded-tempo border border-border"
              >
                <div>
                  <h2 className="text-lg font-semibold text-ink">
                    {shoe.brand} {shoe.model}
                  </h2>
                  <p className="text-sm text-muted">
                    Total: {shoe.totalMileage.toFixed(1)} {shoe.unit}
                  </p>
                </div>
                <Button
                  type="button"
                  size="sm"
                  className="shrink-0"
                  onClick={() => unretireMutation.mutate(shoe.id)}
                  disabled={unretireMutation.isPending}
                >
                  {unretireMutation.isPending ? 'Saving…' : 'Un-retire'}
                </Button>
              </li>
            ))}
          </ul>
        )}
      </Card>
    </PageShell>
  );
}

export default function RetiredShoesPage() {
  return (
    <AuthGuard>
      <RetiredShoesContent />
    </AuthGuard>
  );
}

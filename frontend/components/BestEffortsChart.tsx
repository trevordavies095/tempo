'use client';

import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';
import { getBestEfforts, recalculateBestEfforts } from '@/lib/api';
import Link from 'next/link';
import { IconRefresh } from '@tabler/icons-react';
import { Card } from '@/components/ui/Card';
import { Button } from '@/components/ui/Button';
import { EmptyState } from '@/components/ui/EmptyState';

/// <summary>
/// Format seconds as MM:SS or HH:MM:SS
/// </summary>
function formatTime(seconds: number): string {
  const hours = Math.floor(seconds / 3600);
  const minutes = Math.floor((seconds % 3600) / 60);
  const secs = seconds % 60;

  if (hours > 0) {
    return `${hours}:${minutes.toString().padStart(2, '0')}:${secs.toString().padStart(2, '0')}`;
  }
  return `${minutes}:${secs.toString().padStart(2, '0')}`;
}

function RecalculateConfirm({
  onCancel,
  onConfirm,
  isPending,
}: {
  onCancel: () => void;
  onConfirm: () => void;
  isPending: boolean;
}) {
  return (
    <div className="fixed inset-0 bg-overlay flex items-center justify-center z-50">
      <Card className="max-w-md mx-4">
        <h3 className="text-lg font-semibold text-ink mb-2">
          Recalculate Best Efforts
        </h3>
        <p className="text-sm text-muted mb-4">
          Recalculating best efforts may take a few moments. Continue?
        </p>
        <div className="flex gap-3 justify-end">
          <Button
            variant="secondary"
            size="sm"
            onClick={onCancel}
            disabled={isPending}
          >
            Cancel
          </Button>
          <Button
            size="sm"
            onClick={onConfirm}
            disabled={isPending}
          >
            {isPending ? 'Recalculating...' : 'Recalculate'}
          </Button>
        </div>
      </Card>
    </div>
  );
}

export default function BestEffortsChart() {
  const [showRecalculateConfirm, setShowRecalculateConfirm] = useState(false);
  const queryClient = useQueryClient();

  const { data, isLoading, isError, error } = useQuery({
    queryKey: ['bestEfforts'],
    queryFn: () => getBestEfforts(),
  });

  const recalculateMutation = useMutation({
    mutationFn: recalculateBestEfforts,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['bestEfforts'] });
      setShowRecalculateConfirm(false);
    },
  });

  const handleRecalculate = () => {
    setShowRecalculateConfirm(true);
  };

  const handleConfirmRecalculate = () => {
    recalculateMutation.mutate();
  };

  const handleCancelRecalculate = () => {
    setShowRecalculateConfirm(false);
  };

  if (isLoading) {
    return (
      <Card>
        <div className="flex items-center justify-between mb-4">
          <h2 className="text-lg font-semibold text-ink">
            Best Efforts
          </h2>
        </div>
        <EmptyState title="Loading..." />
      </Card>
    );
  }

  if (isError) {
    return (
      <Card>
        <div className="flex items-center justify-between mb-4">
          <h2 className="text-lg font-semibold text-ink">
            Best Efforts
          </h2>
        </div>
        <EmptyState
          title="Could not load best efforts"
          description={error instanceof Error ? error.message : 'Failed to load best efforts'}
        />
      </Card>
    );
  }

  if (!data || !data.distances || data.distances.length === 0) {
    return (
      <Card>
        <div className="flex items-center gap-2 mb-4">
          <h2 className="text-lg font-semibold text-ink">
            Best Efforts
          </h2>
          <button
            onClick={handleRecalculate}
            className="p-1.5 text-muted hover:text-ink transition-colors"
            title="Recalculate best efforts"
          >
            <IconRefresh className="w-4 h-4" />
          </button>
        </div>
        <EmptyState
          title="No best efforts yet"
          description='Click "Recalculate" to calculate best efforts from your workouts.'
        />
        {showRecalculateConfirm && (
          <RecalculateConfirm
            onCancel={handleCancelRecalculate}
            onConfirm={handleConfirmRecalculate}
            isPending={recalculateMutation.isPending}
          />
        )}
      </Card>
    );
  }

  // Sort by distance in meters (ascending)
  const sortedData = [...data.distances].sort((a, b) => a.distanceM - b.distanceM);

  return (
    <Card>
      <div className="flex items-center gap-2 mb-4">
        <h2 className="text-lg font-semibold text-ink">
          Best Efforts
        </h2>
        <button
          onClick={handleRecalculate}
          disabled={recalculateMutation.isPending}
          className="p-1.5 text-muted hover:text-ink disabled:opacity-50 disabled:cursor-not-allowed transition-colors"
          title="Recalculate best efforts"
        >
          <IconRefresh className={`w-4 h-4 ${recalculateMutation.isPending ? 'animate-spin' : ''}`} />
        </button>
      </div>

      <div className="overflow-x-auto">
        <table className="w-full">
          <tbody className="divide-y divide-border">
            {sortedData.map((item) => (
              <tr key={item.distance}>
                <td className="px-3 py-2 whitespace-nowrap text-sm font-medium text-ink">
                  {item.distance}
                </td>
                <td className="px-3 py-2 whitespace-nowrap text-sm">
                  <Link
                    href={`/dashboard/${item.workoutId}`}
                    className="text-ink underline decoration-border hover:decoration-ink"
                  >
                    {formatTime(item.timeS)}
                  </Link>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>

      {showRecalculateConfirm && (
        <RecalculateConfirm
          onCancel={handleCancelRecalculate}
          onConfirm={handleConfirmRecalculate}
          isPending={recalculateMutation.isPending}
        />
      )}
    </Card>
  );
}

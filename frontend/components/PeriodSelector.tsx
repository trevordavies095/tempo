import { type AvailablePeriod } from '@/lib/api';

interface PeriodSelectorProps {
  availablePeriods: AvailablePeriod[] | undefined;
  selectedPeriodEndDate: string | null;
  onPeriodChange: (periodEndDate: string | null) => void;
  isLoading?: boolean;
  isError?: boolean;
}

export default function PeriodSelector({
  availablePeriods,
  selectedPeriodEndDate,
  onPeriodChange,
  isLoading = false,
  isError = false,
}: PeriodSelectorProps) {
  if (isLoading) {
    return (
      <div className="px-3 py-1.5 text-sm text-muted">
        Loading periods...
      </div>
    );
  }

  if (isError) {
    return (
      <div className="px-3 py-1.5 text-sm text-danger">
        Error loading periods
      </div>
    );
  }

  if (!availablePeriods || availablePeriods.length === 0) {
    return (
      <div className="px-3 py-1.5 text-sm text-muted">
        No periods available
      </div>
    );
  }

  return (
    <select
      value={selectedPeriodEndDate || ''}
      onChange={(e) => onPeriodChange(e.target.value || null)}
      className="px-3 py-1.5 text-sm border border-border rounded-tempo bg-raised text-ink focus:outline-none focus:ring-2 focus:ring-volt"
    >
      {availablePeriods.map((period) => (
        <option key={period.periodEndDate} value={period.periodEndDate}>
          {period.dateRangeLabel}
        </option>
      ))}
    </select>
  );
}

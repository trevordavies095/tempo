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
      <div className="px-3 py-1.5 text-sm text-gray-600 dark:text-gray-400">
        Loading periods...
      </div>
    );
  }

  if (isError) {
    return (
      <div className="px-3 py-1.5 text-sm text-red-600 dark:text-red-400">
        Error loading periods
      </div>
    );
  }

  if (!availablePeriods || availablePeriods.length === 0) {
    return (
      <div className="px-3 py-1.5 text-sm text-gray-600 dark:text-gray-400">
        No periods available
      </div>
    );
  }

  return (
    <select
      value={selectedPeriodEndDate || ''}
      onChange={(e) => onPeriodChange(e.target.value || null)}
      className="px-3 py-1.5 text-sm border border-gray-300 dark:border-gray-600 rounded-lg bg-white dark:bg-gray-700 text-gray-900 dark:text-gray-100 focus:outline-none focus:ring-2 focus:ring-blue-500"
    >
      {availablePeriods.map((period) => (
        <option key={period.periodEndDate} value={period.periodEndDate}>
          {period.dateRangeLabel}
        </option>
      ))}
    </select>
  );
}


import { DATE_PRESET_OPTIONS, type DatePreset } from '@/hooks/useActivitiesFilters';
import { Button } from '@/components/ui/Button';
import { Card } from '@/components/ui/Card';
import { RUN_TYPES } from '@/lib/runTypes';

const RUN_TYPE_OPTIONS = [
  { value: '', label: 'All Run Types' },
  ...RUN_TYPES.map((type) => ({ value: type, label: type })),
];

const fieldClass =
  'flex-1 px-4 py-2 border border-border rounded-tempo bg-raised text-ink placeholder:text-muted focus:outline-none focus:ring-2 focus:ring-volt';

const selectClass =
  'w-full px-4 py-2 border border-border rounded-tempo bg-raised text-ink focus:outline-none focus:ring-2 focus:ring-volt';

const labelClass = 'block text-sm font-medium text-ink mb-2';

interface ActivitiesFiltersProps {
  searchInput: string;
  onSearchInputChange: (value: string) => void;
  onSearch: () => void;
  onKeyPress: (e: React.KeyboardEvent<HTMLInputElement>) => void;
  runType: string;
  onRunTypeChange: (value: string) => void;
  datePreset: DatePreset;
  onDatePresetChange: (value: DatePreset) => void;
  customStart: string;
  onCustomStartChange: (value: string) => void;
  customEnd: string;
  onCustomEndChange: (value: string) => void;
  hasActiveFilters: boolean;
  onClear: () => void;
}

export default function ActivitiesFilters({
  searchInput,
  onSearchInputChange,
  onSearch,
  onKeyPress,
  runType,
  onRunTypeChange,
  datePreset,
  onDatePresetChange,
  customStart,
  onCustomStartChange,
  customEnd,
  onCustomEndChange,
  hasActiveFilters,
  onClear,
}: ActivitiesFiltersProps) {
  return (
    <Card className="mb-6">
      <div className="flex flex-col sm:flex-row sm:flex-wrap sm:items-end gap-4">
        {/* Keywords Search */}
        <div className="flex-1 min-w-0 sm:min-w-64">
          <label htmlFor="keywords" className={labelClass}>
            Keywords
          </label>
          <div className="flex gap-2">
            <input
              id="keywords"
              type="text"
              value={searchInput}
              onChange={(e) => onSearchInputChange(e.target.value)}
              onKeyPress={onKeyPress}
              placeholder="My Morning Workout"
              className={`${fieldClass} min-w-0`}
            />
            <Button onClick={onSearch} size="sm">
              Search
            </Button>
          </div>
        </div>

        {/* Run Type Filter */}
        <div className="sm:w-48">
          <label htmlFor="runType" className={labelClass}>
            Run Type
          </label>
          <select
            id="runType"
            value={runType}
            onChange={(e) => onRunTypeChange(e.target.value)}
            className={selectClass}
          >
            {RUN_TYPE_OPTIONS.map((type) => (
              <option key={type.value} value={type.value}>
                {type.label}
              </option>
            ))}
          </select>
        </div>

        {/* Date Filter */}
        <div className="sm:w-48">
          <label htmlFor="datePreset" className={labelClass}>
            Date
          </label>
          <select
            id="datePreset"
            value={datePreset}
            onChange={(e) => onDatePresetChange(e.target.value as DatePreset)}
            className={selectClass}
          >
            {DATE_PRESET_OPTIONS.map((option) => (
              <option key={option.value} value={option.value}>
                {option.label}
              </option>
            ))}
          </select>
        </div>

        {datePreset === 'custom' && (
          <div className="flex gap-4">
            <div className="flex-1 sm:flex-none">
              <label htmlFor="dateFrom" className={labelClass}>
                From
              </label>
              <input
                id="dateFrom"
                type="date"
                value={customStart}
                onChange={(e) => onCustomStartChange(e.target.value)}
                className={selectClass}
              />
            </div>
            <div className="flex-1 sm:flex-none">
              <label htmlFor="dateTo" className={labelClass}>
                To
              </label>
              <input
                id="dateTo"
                type="date"
                value={customEnd}
                onChange={(e) => onCustomEndChange(e.target.value)}
                className={selectClass}
              />
            </div>
          </div>
        )}

        {hasActiveFilters && (
          <div className="sm:pb-1">
            <Button variant="ghost" size="sm" onClick={onClear}>
              Clear
            </Button>
          </div>
        )}
      </div>
    </Card>
  );
}

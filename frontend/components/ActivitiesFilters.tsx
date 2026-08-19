import { type SortColumn } from '@/hooks/useActivitiesFilters';
import { Button } from '@/components/ui/Button';
import { Card } from '@/components/ui/Card';

const RUN_TYPES = [
  { value: '', label: 'All Run Types' },
  { value: 'Race', label: 'Race' },
  { value: 'Workout', label: 'Workout' },
  { value: 'Long Run', label: 'Long Run' },
  { value: 'Easy Run', label: 'Easy Run' },
];

const fieldClass =
  'flex-1 px-4 py-2 border border-border rounded-tempo bg-raised text-ink placeholder:text-muted focus:outline-none focus:ring-2 focus:ring-volt';

interface ActivitiesFiltersProps {
  searchInput: string;
  onSearchInputChange: (value: string) => void;
  onSearch: () => void;
  onKeyPress: (e: React.KeyboardEvent<HTMLInputElement>) => void;
  runType: string;
  onRunTypeChange: (value: string) => void;
}

export default function ActivitiesFilters({
  searchInput,
  onSearchInputChange,
  onSearch,
  onKeyPress,
  runType,
  onRunTypeChange,
}: ActivitiesFiltersProps) {
  return (
    <Card className="mb-6">
      <div className="flex flex-col sm:flex-row gap-4">
        {/* Keywords Search */}
        <div className="flex-1">
          <label htmlFor="keywords" className="block text-sm font-medium text-ink mb-2">
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
              className={fieldClass}
            />
            <Button onClick={onSearch} size="sm">
              Search
            </Button>
          </div>
        </div>

        {/* Run Type Filter */}
        <div className="sm:w-48">
          <label htmlFor="runType" className="block text-sm font-medium text-ink mb-2">
            Run Type
          </label>
          <select
            id="runType"
            value={runType}
            onChange={(e) => onRunTypeChange(e.target.value)}
            className="w-full px-4 py-2 border border-border rounded-tempo bg-raised text-ink focus:outline-none focus:ring-2 focus:ring-volt"
          >
            {RUN_TYPES.map((type) => (
              <option key={type.value} value={type.value}>
                {type.label}
              </option>
            ))}
          </select>
        </div>
      </div>
    </Card>
  );
}

import { type SortColumn } from '@/hooks/useActivitiesFilters';

const RUN_TYPES = [
  { value: '', label: 'All Run Types' },
  { value: 'Race', label: 'Race' },
  { value: 'Workout', label: 'Workout' },
  { value: 'Long Run', label: 'Long Run' },
  { value: 'Easy Run', label: 'Easy Run' },
];

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
    <div className="w-full bg-white dark:bg-gray-900 rounded-lg border border-gray-200 dark:border-gray-800 p-6 mb-6">
      <div className="flex flex-col sm:flex-row gap-4">
        {/* Keywords Search */}
        <div className="flex-1">
          <label htmlFor="keywords" className="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-2">
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
              className="flex-1 px-4 py-2 border border-gray-300 dark:border-gray-700 rounded-md bg-white dark:bg-gray-800 text-gray-900 dark:text-gray-100 focus:outline-none focus:ring-2 focus:ring-blue-500 focus:border-transparent"
            />
            <button
              onClick={onSearch}
              className="px-4 py-2 bg-blue-600 text-white rounded-md hover:bg-blue-700 focus:outline-none focus:ring-2 focus:ring-blue-500 focus:ring-offset-2 transition-colors"
            >
              Search
            </button>
          </div>
        </div>

        {/* Run Type Filter */}
        <div className="sm:w-48">
          <label htmlFor="runType" className="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-2">
            Run Type
          </label>
          <select
            id="runType"
            value={runType}
            onChange={(e) => onRunTypeChange(e.target.value)}
            className="w-full px-4 py-2 border border-gray-300 dark:border-gray-700 rounded-md bg-white dark:bg-gray-800 text-gray-900 dark:text-gray-100 focus:outline-none focus:ring-2 focus:ring-blue-500 focus:border-transparent"
          >
            {RUN_TYPES.map((type) => (
              <option key={type.value} value={type.value}>
                {type.label}
              </option>
            ))}
          </select>
        </div>
      </div>
    </div>
  );
}


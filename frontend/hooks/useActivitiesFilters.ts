import { useState, useCallback } from 'react';
import { formatLocalDate } from '@/lib/localCalendar';

export type DatePreset = 'all' | 'last7' | 'last30' | 'custom';

/** Date filter options; wording matches the iOS Search tab. */
export const DATE_PRESET_OPTIONS: ReadonlyArray<{ value: DatePreset; label: string }> = [
  { value: 'all', label: 'All Time' },
  { value: 'last7', label: 'Last 7 Days' },
  { value: 'last30', label: 'Last 30 Days' },
  { value: 'custom', label: 'Custom Range…' },
];

/** Inclusive start/end dates for a rolling preset ending today (local calendar). */
export function presetDateRange(
  preset: 'last7' | 'last30',
  today: Date = new Date()
): { startDate: string; endDate: string } {
  const days = preset === 'last7' ? 6 : 29;
  const start = new Date(today.getFullYear(), today.getMonth(), today.getDate() - days);
  return { startDate: formatLocalDate(start), endDate: formatLocalDate(today) };
}

export type SortColumn = 'startedAt' | 'name' | 'durationS' | 'distanceM' | 'elevGainM' | 'relativeEffort';
export type SortOrder = 'asc' | 'desc';

export function useActivitiesFilters() {
  const [page, setPage] = useState(1);
  const [keyword, setKeyword] = useState('');
  const [searchInput, setSearchInput] = useState('');
  const [runType, setRunType] = useState('');
  const [datePreset, setDatePresetState] = useState<DatePreset>('all');
  const [presetRange, setPresetRange] = useState<{ startDate: string; endDate: string } | null>(null);
  const [customStart, setCustomStartState] = useState('');
  const [customEnd, setCustomEndState] = useState('');
  const [sortBy, setSortBy] = useState<SortColumn>('startedAt');
  const [sortOrder, setSortOrder] = useState<SortOrder>('desc');

  const handleSearch = useCallback(() => {
    setKeyword(searchInput);
    setPage(1);
  }, [searchInput]);

  const handleKeyPress = useCallback(
    (e: React.KeyboardEvent<HTMLInputElement>) => {
      if (e.key === 'Enter') {
        handleSearch();
      }
    },
    [handleSearch]
  );

  const handleSort = useCallback(
    (column: SortColumn) => {
      if (sortBy === column) {
        // Toggle sort order if clicking the same column
        setSortOrder(sortOrder === 'asc' ? 'desc' : 'asc');
      } else {
        // Set new column and default to descending
        setSortBy(column);
        setSortOrder('desc');
      }
      setPage(1);
    },
    [sortBy, sortOrder]
  );

  const handleRunTypeChange = useCallback((value: string) => {
    setRunType(value);
    setPage(1);
  }, []);

  const setDatePreset = useCallback((value: DatePreset) => {
    setDatePresetState(value);
    setPresetRange(value === 'last7' || value === 'last30' ? presetDateRange(value) : null);
    setPage(1);
  }, []);

  // Custom range: both dates must be set; a To before From is swapped so the inputs show the applied range.
  const setCustomRange = useCallback((start: string, end: string) => {
    if (start && end && end < start) {
      setCustomStartState(end);
      setCustomEndState(start);
    } else {
      setCustomStartState(start);
      setCustomEndState(end);
    }
    setPage(1);
  }, []);

  const setCustomStart = useCallback(
    (value: string) => setCustomRange(value, customEnd),
    [customEnd, setCustomRange]
  );

  const setCustomEnd = useCallback(
    (value: string) => setCustomRange(customStart, value),
    [customStart, setCustomRange]
  );

  let startDate: string | undefined;
  let endDate: string | undefined;
  if (datePreset === 'custom') {
    if (customStart && customEnd) {
      startDate = customStart;
      endDate = customEnd;
    }
  } else if (presetRange) {
    startDate = presetRange.startDate;
    endDate = presetRange.endDate;
  }

  const hasActiveFilters = keyword !== '' || runType !== '' || datePreset !== 'all';

  const clearFilters = useCallback(() => {
    setKeyword('');
    setSearchInput('');
    setRunType('');
    setDatePresetState('all');
    setPresetRange(null);
    setCustomStartState('');
    setCustomEndState('');
    setPage(1);
  }, []);

  const getSortParam = useCallback((column: SortColumn): string => {
    const apiColumnMap: Record<SortColumn, string> = {
      startedAt: 'startedAt',
      name: 'name',
      durationS: 'duration',
      distanceM: 'distance',
      elevGainM: 'elevation',
      relativeEffort: 'relativeEffort',
    };
    return apiColumnMap[column];
  }, []);

  return {
    page,
    setPage,
    keyword,
    searchInput,
    setSearchInput,
    runType,
    sortBy,
    sortOrder,
    handleSearch,
    handleKeyPress,
    handleSort,
    handleRunTypeChange,
    datePreset,
    setDatePreset,
    customStart,
    setCustomStart,
    customEnd,
    setCustomEnd,
    startDate,
    endDate,
    hasActiveFilters,
    clearFilters,
    getSortParam,
  };
}


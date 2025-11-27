import { useState, useCallback } from 'react';

export type SortColumn = 'startedAt' | 'name' | 'durationS' | 'distanceM' | 'elevGainM' | 'relativeEffort';
export type SortOrder = 'asc' | 'desc';

export function useActivitiesFilters() {
  const [page, setPage] = useState(1);
  const [keyword, setKeyword] = useState('');
  const [searchInput, setSearchInput] = useState('');
  const [runType, setRunType] = useState('');
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
    getSortParam,
  };
}


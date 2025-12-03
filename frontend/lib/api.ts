const API_BASE_URL = '/api';

// Auth interfaces
export interface LoginRequest {
  username: string;
  password: string;
}

export interface RegisterRequest {
  username: string;
  password: string;
}

export interface AuthResponse {
  userId: string;
  username: string;
  expiresAt: string;
}

export interface UserInfo {
  userId: string;
  username: string;
  createdAt: string;
  lastLoginAt: string | null;
}

export interface RegistrationAvailableResponse {
  registrationAvailable: boolean;
}

export interface WorkoutImportResponse {
  id: string;
  startedAt: string;
  durationS: number;
  distanceM: number;
  avgPaceS: number;
  elevGainM: number | null;
  splitsCount: number;
}

export interface WorkoutListItem {
  id: string;
  startedAt: string;
  durationS: number;
  distanceM: number;
  avgPaceS: number;
  elevGainM: number | null;
  elevLossM: number | null;
  minElevM: number | null;
  maxElevM: number | null;
  maxSpeedMps: number | null;
  avgSpeedMps: number | null;
  movingTimeS: number | null;
  maxHeartRateBpm: number | null;
  avgHeartRateBpm: number | null;
  minHeartRateBpm: number | null;
  maxCadenceRpm: number | null;
  avgCadenceRpm: number | null;
  maxPowerWatts: number | null;
  avgPowerWatts: number | null;
  calories: number | null;
  relativeEffort: number | null;
  runType: string | null;
  source: string | null;
  device: string | null;
  name: string | null;
  hasRoute: boolean;
  route: {
    type: string;
    coordinates: [number, number][];
  } | null;
  splitsCount: number;
}

export interface WorkoutsListResponse {
  items: WorkoutListItem[];
  totalCount: number;
  page: number;
  pageSize: number;
  totalPages: number;
}

export interface WorkoutsListParams {
  page?: number;
  pageSize?: number;
  startDate?: string;
  endDate?: string;
  minDistanceM?: number;
  maxDistanceM?: number;
  keyword?: string;
  runType?: string;
  sortBy?: string;
  sortOrder?: 'asc' | 'desc';
}

export interface WorkoutDetail {
  id: string;
  startedAt: string;
  durationS: number;
  distanceM: number;
  avgPaceS: number;
  elevGainM: number | null;
  elevLossM: number | null;
  minElevM: number | null;
  maxElevM: number | null;
  maxSpeedMps: number | null;
  avgSpeedMps: number | null;
  movingTimeS: number | null;
  maxHeartRateBpm: number | null;
  avgHeartRateBpm: number | null;
  minHeartRateBpm: number | null;
  maxCadenceRpm: number | null;
  avgCadenceRpm: number | null;
  maxPowerWatts: number | null;
  avgPowerWatts: number | null;
  calories: number | null;
  relativeEffort: number | null;
  runType: string | null;
  notes: string | null;
  source: string | null;
  device: string | null;
  name: string | null;
  weather: any | null;
  rawGpxData: any | null;
  rawFitData: any | null;
  rawStravaData: any | null;
  createdAt: string;
  shoeId: string | null;
  shoe: {
    id: string;
    brand: string;
    model: string;
  } | null;
  route: {
    type: string;
    coordinates: [number, number][];
  } | null;
  splits: Array<{
    idx: number;
    distanceM: number;
    durationS: number;
    paceS: number;
  }>;
}

export interface WorkoutMedia {
  id: string;
  filename: string;
  mimeType: string;
  fileSizeBytes: number;
  caption: string | null;
  createdAt: string;
}

export interface WorkoutImportSummaryResponse {
  totalProcessed: number;
  successful: number;
  skipped: number;
  updated: number;
  errors: number;
  errorDetails: Array<{ filename: string; error: string }>;
}

export async function importWorkoutFile(
  files: File | File[], 
  unitPreference?: 'metric' | 'imperial'
): Promise<WorkoutImportResponse | WorkoutImportSummaryResponse> {
  const formData = new FormData();
  
  // Handle both single file (backward compat) and multiple files
  const fileArray = Array.isArray(files) ? files : [files];
  
  // Append all files with the same field name to support multiple files
  fileArray.forEach(file => {
    formData.append('file', file);
  });
  
  if (unitPreference) {
    formData.append('unitPreference', unitPreference);
  }

  const response = await fetch(`${API_BASE_URL}/workouts/import`, {
    method: 'POST',
    body: formData,
    credentials: 'include',
  });

  if (!response.ok) {
    const error = await response.json().catch(() => ({ error: 'Failed to import workout file' }));
    throw new Error(error.error || `HTTP error! status: ${response.status}`);
  }

  return response.json();
}

/**
 * Builds a URLSearchParams object from WorkoutsListParams
 * @param params - Optional workout list parameters
 * @returns URLSearchParams object
 */
function buildQueryParams(params?: WorkoutsListParams): URLSearchParams {
  const searchParams = new URLSearchParams();
  
  if (params?.page) {
    searchParams.set('page', params.page.toString());
  }
  if (params?.pageSize) {
    searchParams.set('pageSize', params.pageSize.toString());
  }
  if (params?.startDate) {
    searchParams.set('startDate', params.startDate);
  }
  if (params?.endDate) {
    searchParams.set('endDate', params.endDate);
  }
  if (params?.minDistanceM !== undefined) {
    searchParams.set('minDistanceM', params.minDistanceM.toString());
  }
  if (params?.maxDistanceM !== undefined) {
    searchParams.set('maxDistanceM', params.maxDistanceM.toString());
  }
  if (params?.keyword) {
    searchParams.set('keyword', params.keyword);
  }
  if (params?.runType) {
    searchParams.set('runType', params.runType);
  }
  if (params?.sortBy) {
    searchParams.set('sortBy', params.sortBy);
  }
  if (params?.sortOrder) {
    searchParams.set('sortOrder', params.sortOrder);
  }

  return searchParams;
}

export async function getWorkouts(
  params?: WorkoutsListParams
): Promise<WorkoutsListResponse> {
  const searchParams = buildQueryParams(params);
  const queryString = searchParams.toString();
  const url = `${API_BASE_URL}/workouts${queryString ? `?${queryString}` : ''}`;

  const response = await fetch(url, {
    method: 'GET',
    headers: { 'Content-Type': 'application/json' },
    credentials: 'include',
  });

  if (!response.ok) {
    if (response.status === 404) {
      throw new Error('Page not found');
    }
    throw new Error(`Failed to fetch workouts: ${response.status}`);
  }

  return response.json();
}

export async function getWorkout(id: string): Promise<WorkoutDetail> {
  const response = await fetch(`${API_BASE_URL}/workouts/${id}`, {
    method: 'GET',
    headers: { 'Content-Type': 'application/json' },
    credentials: 'include',
  });

  if (response.status === 404) {
    throw new Error('Workout not found');
  }

  if (!response.ok) {
    throw new Error(`Failed to fetch workout: ${response.status}`);
  }

  return response.json();
}

export interface BulkImportResponse {
  totalProcessed: number;
  successful: number;
  skipped: number;
  errors: number;
  errorDetails: Array<{
    filename: string;
    error: string;
  }>;
}

// Get direct API URL for large file uploads (bypasses Next.js rewrites to avoid 10MB limit)
function getDirectApiUrl(): string {
  // Use environment variable if available, otherwise determine based on current host
  if (typeof window !== 'undefined') {
    // Client-side: use the same host and port as the current page, but point to API port
    const host = window.location.hostname;
    // In Docker, API is on port 5001; in development, also on 5001
    // If we're accessing via a different port (e.g., 3000 for frontend), use localhost:5001
    if (host === 'localhost' || host === '127.0.0.1') {
      return 'http://localhost:5001';
    }
    // In production, assume API is on the same host but port 5001
    return `${window.location.protocol}//${host}:5001`;
  }
  // Server-side fallback
  return process.env.NEXT_PUBLIC_API_URL || 'http://localhost:5001';
}

export async function exportAllData(): Promise<Blob> {
  const response = await fetch(`${API_BASE_URL}/workouts/export`, {
    method: 'POST',
    credentials: 'include',
  });

  if (!response.ok) {
    const error = await response.json().catch(() => ({ error: `HTTP error! status: ${response.status}` }));
    throw new Error(error.error || `Failed to export data: ${response.status}`);
  }

  return response.blob();
}

export async function importBulkStravaExport(zipFile: File, unitPreference?: 'metric' | 'imperial'): Promise<BulkImportResponse> {
  const formData = new FormData();
  formData.append('file', zipFile);
  if (unitPreference) {
    formData.append('unitPreference', unitPreference);
  }

  // Use direct API URL to bypass Next.js rewrites and avoid 10MB body size limit
  const directApiUrl = getDirectApiUrl();
  const response = await fetch(`${directApiUrl}/workouts/import/bulk`, {
    method: 'POST',
    body: formData,
    credentials: 'include',
  });

  if (!response.ok) {
    const error = await response.json().catch(() => ({ error: 'Failed to import Strava export' }));
    throw new Error(error.error || `HTTP error! status: ${response.status}`);
  }

  return response.json();
}

export async function getWorkoutMedia(workoutId: string): Promise<WorkoutMedia[]> {
  const response = await fetch(`${API_BASE_URL}/workouts/${workoutId}/media`, {
    method: 'GET',
    headers: { 'Content-Type': 'application/json' },
    credentials: 'include',
  });

  // If workout not found, return empty array (no media)
  if (response.status === 404) {
    return [];
  }

  if (!response.ok) {
    throw new Error(`Failed to fetch workout media: ${response.status}`);
  }

  const data = await response.json();
  return data;
}

export function getWorkoutMediaUrl(workoutId: string, mediaId: string): string {
  return `${API_BASE_URL}/workouts/${workoutId}/media/${mediaId}`;
}

export async function deleteWorkoutMedia(
  workoutId: string,
  mediaId: string
): Promise<void> {
  const response = await fetch(`${API_BASE_URL}/workouts/${workoutId}/media/${mediaId}`, {
    method: 'DELETE',
    credentials: 'include',
  });

  if (response.status === 404) {
    throw new Error('Media not found');
  }

  if (!response.ok) {
    const error = await response.json().catch(() => ({ error: `HTTP error! status: ${response.status}` }));
    throw new Error(error.error || `Failed to delete media: ${response.status}`);
  }
}

export async function uploadWorkoutMedia(
  workoutId: string,
  files: File[]
): Promise<WorkoutMedia[]> {
  if (files.length === 0) {
    throw new Error('No files provided');
  }

  const formData = new FormData();
  files.forEach((file) => {
    formData.append('files', file);
  });

  const response = await fetch(`${API_BASE_URL}/workouts/${workoutId}/media`, {
    method: 'POST',
    body: formData,
    credentials: 'include',
  });

  if (response.status === 404) {
    throw new Error('Workout not found');
  }

  if (!response.ok) {
    const error = await response.json().catch(() => ({ error: `HTTP error! status: ${response.status}` }));
    throw new Error(error.error || `Failed to upload media: ${response.status}`);
  }

  const data = await response.json();
  
  // Handle response format: could be array directly or object with 'uploaded' property
  if (Array.isArray(data)) {
    return data;
  } else if (data.uploaded && Array.isArray(data.uploaded)) {
    return data.uploaded;
  } else {
    throw new Error('Unexpected response format from upload endpoint');
  }
}

export interface WeeklyStatsResponse {
  weekStart: string;
  weekEnd: string;
  dailyMiles: number[]; // [Monday, Tuesday, Wednesday, Thursday, Friday, Saturday, Sunday]
}

export interface RelativeEffortStatsResponse {
  weekStart: string;
  weekEnd: string;
  currentWeek: number[]; // Cumulative relative effort [Monday, Tuesday, ..., Sunday]
  previousWeeks: number[]; // Total relative effort for each of the 3 previous weeks
  threeWeekAverage: number;
  rangeMin: number;
  rangeMax: number;
  currentWeekTotal: number;
}

export interface YearlyStatsResponse {
  currentYear: number;
  previousYear: number;
  currentYearLabel: string;
  previousYearLabel: string;
}

export async function getWeeklyStats(timezoneOffsetMinutes?: number): Promise<WeeklyStatsResponse> {
  const searchParams = new URLSearchParams();
  if (timezoneOffsetMinutes !== undefined) {
    searchParams.set('timezoneOffsetMinutes', timezoneOffsetMinutes.toString());
  }

  const queryString = searchParams.toString();
  const url = `${API_BASE_URL}/workouts/stats/weekly${queryString ? `?${queryString}` : ''}`;

  const response = await fetch(url, {
    method: 'GET',
    headers: { 'Content-Type': 'application/json' },
    credentials: 'include',
  });

  if (!response.ok) {
    throw new Error(`Failed to fetch weekly stats: ${response.status}`);
  }

  return response.json();
}

export async function getRelativeEffortStats(timezoneOffsetMinutes?: number): Promise<RelativeEffortStatsResponse> {
  const searchParams = new URLSearchParams();
  if (timezoneOffsetMinutes !== undefined) {
    searchParams.set('timezoneOffsetMinutes', timezoneOffsetMinutes.toString());
  }

  const queryString = searchParams.toString();
  const url = `${API_BASE_URL}/workouts/stats/relative-effort${queryString ? `?${queryString}` : ''}`;

  const response = await fetch(url, {
    method: 'GET',
    headers: { 'Content-Type': 'application/json' },
    credentials: 'include',
  });

  if (!response.ok) {
    throw new Error(`Failed to fetch relative effort stats: ${response.status}`);
  }

  return response.json();
}

export interface BestEffortItem {
  distance: string;
  distanceM: number;
  timeS: number;
  workoutId: string;
  workoutDate: string;
}

export interface BestEffortsResponse {
  distances: BestEffortItem[];
}

export async function getBestEfforts(): Promise<BestEffortsResponse> {
  const url = `${API_BASE_URL}/workouts/stats/best-efforts`;

  const response = await fetch(url, {
    method: 'GET',
    headers: { 'Content-Type': 'application/json' },
    credentials: 'include',
  });

  if (!response.ok) {
    throw new Error(`Failed to fetch best efforts: ${response.status}`);
  }

  return response.json();
}

export interface RecalculateBestEffortsResponse {
  message: string;
  count: number;
}

export async function recalculateBestEfforts(): Promise<RecalculateBestEffortsResponse> {
  const url = `${API_BASE_URL}/workouts/stats/best-efforts/recalculate`;

  const response = await fetch(url, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    credentials: 'include',
  });

  if (!response.ok) {
    const error = await response.json().catch(() => ({ error: `HTTP error! status: ${response.status}` }));
    throw new Error(error.error || `Failed to recalculate best efforts: ${response.status}`);
  }

  return response.json();
}

export async function getYearlyStats(timezoneOffsetMinutes?: number): Promise<YearlyStatsResponse> {
  const searchParams = new URLSearchParams();
  if (timezoneOffsetMinutes !== undefined) {
    searchParams.set('timezoneOffsetMinutes', timezoneOffsetMinutes.toString());
  }

  const queryString = searchParams.toString();
  const url = `${API_BASE_URL}/workouts/stats/yearly${queryString ? `?${queryString}` : ''}`;

  const response = await fetch(url, {
    method: 'GET',
    headers: { 'Content-Type': 'application/json' },
    credentials: 'include',
  });

  if (!response.ok) {
    throw new Error(`Failed to fetch yearly stats: ${response.status}`);
  }

  return response.json();
}

export interface YearlyWeeklyStatsItem {
  weekNumber: number;
  weekStart: string;
  weekEnd: string;
  distanceM: number;
}

export interface YearlyWeeklyStatsResponse {
  weeks: YearlyWeeklyStatsItem[];
  dateRangeStart: string;
  dateRangeEnd: string;
}

export async function getYearlyWeeklyStats(
  periodEndDate?: string,
  timezoneOffsetMinutes?: number
): Promise<YearlyWeeklyStatsResponse> {
  const searchParams = new URLSearchParams();
  if (periodEndDate) {
    searchParams.set('periodEndDate', periodEndDate);
  }
  if (timezoneOffsetMinutes !== undefined) {
    searchParams.set('timezoneOffsetMinutes', timezoneOffsetMinutes.toString());
  }

  const queryString = searchParams.toString();
  const url = `${API_BASE_URL}/workouts/stats/yearly-weekly${queryString ? `?${queryString}` : ''}`;

  const response = await fetch(url, {
    method: 'GET',
    headers: { 'Content-Type': 'application/json' },
    credentials: 'include',
  });

  if (!response.ok) {
    throw new Error(`Failed to fetch yearly weekly stats: ${response.status}`);
  }

  return response.json();
}

export interface AvailablePeriod {
  periodEndDate: string;
  dateRangeStart: string;
  dateRangeEnd: string;
  dateRangeLabel: string;
}

export async function getAvailablePeriods(
  timezoneOffsetMinutes?: number
): Promise<AvailablePeriod[]> {
  const searchParams = new URLSearchParams();
  if (timezoneOffsetMinutes !== undefined) {
    searchParams.set('timezoneOffsetMinutes', timezoneOffsetMinutes.toString());
  }

  const queryString = searchParams.toString();
  const url = `${API_BASE_URL}/workouts/stats/available-periods${queryString ? `?${queryString}` : ''}`;

  const response = await fetch(url, {
    method: 'GET',
    headers: { 'Content-Type': 'application/json' },
    credentials: 'include',
  });

  if (!response.ok) {
    throw new Error(`Failed to fetch available periods: ${response.status}`);
  }

  return response.json();
}

export async function getAvailableYears(): Promise<number[]> {
  const url = `${API_BASE_URL}/workouts/stats/available-years`;

  const response = await fetch(url, {
    method: 'GET',
    headers: { 'Content-Type': 'application/json' },
    credentials: 'include',
  });

  if (!response.ok) {
    throw new Error(`Failed to fetch available years: ${response.status}`);
  }

  return response.json();
}

export interface UpdateWorkoutRequest {
  runType?: string | null;
  notes?: string | null;
  name?: string | null;
  shoeId?: string | null;
}

export interface UpdateWorkoutResponse {
  id: string;
  runType: string | null;
  notes: string | null;
  name: string | null;
  shoeId: string | null;
}

export async function updateWorkout(
  id: string,
  updates: UpdateWorkoutRequest
): Promise<UpdateWorkoutResponse> {
  const response = await fetch(`${API_BASE_URL}/workouts/${id}`, {
    method: 'PATCH',
    headers: { 'Content-Type': 'application/json' },
    credentials: 'include',
    body: JSON.stringify(updates),
  });

  if (response.status === 404) {
    throw new Error('Workout not found');
  }

  if (!response.ok) {
    const error = await response.json().catch(() => ({ error: `HTTP error! status: ${response.status}` }));
    throw new Error(error.error || `Failed to update workout: ${response.status}`);
  }

  return response.json();
}

export async function deleteWorkout(id: string): Promise<void> {
  const response = await fetch(`${API_BASE_URL}/workouts/${id}`, {
    method: 'DELETE',
    credentials: 'include',
  });

  if (response.status === 404) {
    throw new Error('Workout not found');
  }

  if (!response.ok) {
    const error = await response.json().catch(() => ({ error: `HTTP error! status: ${response.status}` }));
    throw new Error(error.error || `Failed to delete workout: ${response.status}`);
  }
}

// Heart Rate Zones
export type HeartRateCalculationMethod = 'AgeBased' | 'Karvonen' | 'Custom';

export interface HeartRateZone {
  zoneNumber: number;
  minBpm: number;
  maxBpm: number;
}

export interface HeartRateZoneSettings {
  calculationMethod: HeartRateCalculationMethod;
  age: number | null;
  restingHeartRateBpm: number | null;
  maxHeartRateBpm: number | null;
  zones: HeartRateZone[];
  isFirstTimeSetup?: boolean;
}

export interface UpdateHeartRateZoneSettingsRequest {
  calculationMethod: HeartRateCalculationMethod;
  age?: number | null;
  restingHeartRateBpm?: number | null;
  maxHeartRateBpm?: number | null;
  zones?: HeartRateZone[];
}

export async function getHeartRateZones(): Promise<HeartRateZoneSettings> {
  const response = await fetch(`${API_BASE_URL}/settings/heart-rate-zones`, {
    method: 'GET',
    headers: { 'Content-Type': 'application/json' },
    credentials: 'include',
  });

  if (!response.ok) {
    throw new Error(`Failed to fetch heart rate zones: ${response.status}`);
  }

  return response.json();
}

export async function updateHeartRateZones(
  settings: UpdateHeartRateZoneSettingsRequest
): Promise<HeartRateZoneSettings> {
  const response = await fetch(`${API_BASE_URL}/settings/heart-rate-zones`, {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json' },
    credentials: 'include',
    body: JSON.stringify(settings),
  });

  if (!response.ok) {
    const error = await response.json().catch(() => ({ error: `HTTP error! status: ${response.status}` }));
    throw new Error(error.error || `Failed to update heart rate zones: ${response.status}`);
  }

  return response.json();
}

export interface UpdateHeartRateZonesWithRecalcRequest extends UpdateHeartRateZoneSettingsRequest {
  recalculateExisting?: boolean;
}

export interface UpdateHeartRateZonesWithRecalcResponse extends HeartRateZoneSettings {
  recalculatedCount?: number | null;
  recalculatedErrorCount?: number | null;
}

export async function updateHeartRateZonesWithRecalc(
  settings: UpdateHeartRateZonesWithRecalcRequest
): Promise<UpdateHeartRateZonesWithRecalcResponse> {
  const response = await fetch(`${API_BASE_URL}/settings/heart-rate-zones/update-with-recalc`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    credentials: 'include',
    body: JSON.stringify(settings),
  });

  if (!response.ok) {
    const error = await response.json().catch(() => ({ error: `HTTP error! status: ${response.status}` }));
    throw new Error(error.error || `Failed to update heart rate zones: ${response.status}`);
  }

  return response.json();
}

export interface RecalculateRelativeEffortResponse {
  updatedCount: number;
  totalQualifyingWorkouts: number;
  errorCount: number;
  errors?: string[];
  message?: string;
}

export async function getQualifyingWorkoutCount(): Promise<{ count: number }> {
  const response = await fetch(`${API_BASE_URL}/settings/recalculate-relative-effort/count`, {
    method: 'GET',
    headers: { 'Content-Type': 'application/json' },
    credentials: 'include',
  });

  if (!response.ok) {
    throw new Error(`Failed to get qualifying workout count: ${response.status}`);
  }

  return response.json();
}

export async function recalculateAllRelativeEffort(): Promise<RecalculateRelativeEffortResponse> {
  const response = await fetch(`${API_BASE_URL}/settings/recalculate-relative-effort`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    credentials: 'include',
  });

  if (!response.ok) {
    const error = await response.json().catch(() => ({ error: `HTTP error! status: ${response.status}` }));
    throw new Error(error.error || `Failed to recalculate relative effort: ${response.status}`);
  }

  return response.json();
}

export async function getUnitPreference(): Promise<{ unitPreference: 'metric' | 'imperial' }> {
  const response = await fetch(`${API_BASE_URL}/settings/unit-preference`, {
    method: 'GET',
    headers: { 'Content-Type': 'application/json' },
    credentials: 'include',
  });

  if (!response.ok) {
    throw new Error(`Failed to get unit preference: ${response.status}`);
  }

  return response.json();
}

export async function updateUnitPreference(unitPreference: 'metric' | 'imperial'): Promise<{ unitPreference: 'metric' | 'imperial' }> {
  const response = await fetch(`${API_BASE_URL}/settings/unit-preference`, {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json' },
    credentials: 'include',
    body: JSON.stringify({ unitPreference }),
  });

  if (!response.ok) {
    const error = await response.json().catch(() => ({ error: `HTTP error! status: ${response.status}` }));
    throw new Error(error.error || `Failed to update unit preference: ${response.status}`);
  }

  return response.json();
}

export async function getQualifyingWorkoutCountForSplits(): Promise<{ count: number }> {
  const response = await fetch(`${API_BASE_URL}/settings/recalculate-splits/count`, {
    method: 'GET',
    headers: { 'Content-Type': 'application/json' },
    credentials: 'include',
  });

  if (!response.ok) {
    throw new Error(`Failed to get workout count for split recalculation: ${response.status}`);
  }

  return response.json();
}

export interface RecalculateSplitsResponse {
  updatedCount: number;
  totalWorkouts: number;
  errorCount: number;
  errors?: string[] | null;
}

export async function recalculateAllSplits(): Promise<RecalculateSplitsResponse> {
  const response = await fetch(`${API_BASE_URL}/settings/recalculate-splits`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    credentials: 'include',
  });

  if (!response.ok) {
    const error = await response.json().catch(() => ({ error: `HTTP error! status: ${response.status}` }));
    throw new Error(error.error || `Failed to recalculate splits: ${response.status}`);
  }

  return response.json();
}

export async function recalculateWorkoutSplits(workoutId: string): Promise<{ id: string; splitsCount: number }> {
  const response = await fetch(`${API_BASE_URL}/workouts/${workoutId}/recalculate-splits`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    credentials: 'include',
  });

  if (!response.ok) {
    const error = await response.json().catch(() => ({ error: `HTTP error! status: ${response.status}` }));
    throw new Error(error.error || `Failed to recalculate splits for workout: ${response.status}`);
  }

  return response.json();
}

export interface CropWorkoutRequest {
  startTrimSeconds: number;
  endTrimSeconds: number;
}

export async function cropWorkout(
  workoutId: string,
  startTrimSeconds: number,
  endTrimSeconds: number
): Promise<WorkoutDetail> {
  const response = await fetch(`${API_BASE_URL}/workouts/${workoutId}/crop`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    credentials: 'include',
    body: JSON.stringify({ startTrimSeconds, endTrimSeconds }),
  });

  if (response.status === 404) {
    throw new Error('Workout not found');
  }

  if (!response.ok) {
    const error = await response.json().catch(() => ({ error: `HTTP error! status: ${response.status}` }));
    throw new Error(error.error || `Failed to crop workout: ${response.status}`);
  }

  return response.json();
}

export interface VersionResponse {
  version: string;
  buildDate: string;
  gitCommit: string;
}

export async function getVersion(): Promise<VersionResponse> {
  const response = await fetch(`${API_BASE_URL}/version`, {
    method: 'GET',
    headers: { 'Content-Type': 'application/json' },
    credentials: 'include',
  });

  if (!response.ok) {
    throw new Error(`Failed to fetch version: ${response.status}`);
  }

  return response.json();
}

// Authentication functions
export async function login(username: string, password: string): Promise<AuthResponse> {
  const response = await fetch(`${API_BASE_URL}/auth/login`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    credentials: 'include',
    body: JSON.stringify({ username, password }),
  });

  if (!response.ok) {
    if (response.status === 401) {
      throw new Error('Invalid username or password');
    }
    const error = await response.json().catch(() => ({ error: 'Login failed' }));
    throw new Error(error.error || `Login failed: ${response.status}`);
  }

  return response.json();
}

export async function register(username: string, password: string): Promise<{ message: string; userId: string }> {
  const response = await fetch(`${API_BASE_URL}/auth/register`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    credentials: 'include',
    body: JSON.stringify({ username, password }),
  });

  if (!response.ok) {
    const error = await response.json().catch(() => ({ error: 'Registration failed' }));
    throw new Error(error.error || `Registration failed: ${response.status}`);
  }

  return response.json();
}

export async function getCurrentUser(): Promise<UserInfo> {
  const response = await fetch(`${API_BASE_URL}/auth/me`, {
    method: 'GET',
    headers: { 'Content-Type': 'application/json' },
    credentials: 'include',
  });

  if (response.status === 401) {
    throw new Error('Not authenticated');
  }

  if (!response.ok) {
    throw new Error(`Failed to get current user: ${response.status}`);
  }

  return response.json();
}

export async function logout(): Promise<void> {
  const response = await fetch(`${API_BASE_URL}/auth/logout`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    credentials: 'include',
  });

  if (!response.ok) {
    throw new Error(`Logout failed: ${response.status}`);
  }
}

export async function checkRegistrationAvailable(): Promise<RegistrationAvailableResponse> {
  const response = await fetch(`${API_BASE_URL}/auth/registration-available`, {
    method: 'GET',
    headers: { 'Content-Type': 'application/json' },
    credentials: 'include',
  });

  if (!response.ok) {
    throw new Error(`Failed to check registration availability: ${response.status}`);
  }

  return response.json();
}

// Shoe interfaces
export interface Shoe {
  id: string;
  brand: string;
  model: string;
  initialMileageM: number | null;
  totalMileage: number;
  unit: 'km' | 'miles';
  createdAt: string;
  updatedAt: string;
}

export interface ShoeWithMileage {
  id: string;
  brand: string;
  model: string;
  totalMileage: number;
  unit: 'km' | 'miles';
}

export interface CreateShoeRequest {
  brand: string;
  model: string;
  initialMileageM?: number | null;
}

export interface UpdateShoeRequest {
  brand?: string;
  model?: string;
  initialMileageM?: number | null;
}

export interface ShoeMileageResponse {
  shoeId: string;
  totalMileage: number;
  unit: 'km' | 'miles';
}

export interface DefaultShoeResponse {
  defaultShoeId: string | null;
  brand?: string;
  model?: string;
}

// Shoe API functions
export async function getShoes(): Promise<Shoe[]> {
  const response = await fetch(`${API_BASE_URL}/shoes`, {
    method: 'GET',
    headers: { 'Content-Type': 'application/json' },
    credentials: 'include',
  });

  if (!response.ok) {
    throw new Error(`Failed to fetch shoes: ${response.status}`);
  }

  return response.json();
}

export async function createShoe(shoe: CreateShoeRequest): Promise<Shoe> {
  const response = await fetch(`${API_BASE_URL}/shoes`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    credentials: 'include',
    body: JSON.stringify(shoe),
  });

  if (!response.ok) {
    const error = await response.json().catch(() => ({ error: `HTTP error! status: ${response.status}` }));
    throw new Error(error.error || `Failed to create shoe: ${response.status}`);
  }

  return response.json();
}

export async function updateShoe(id: string, shoe: UpdateShoeRequest): Promise<Shoe> {
  const response = await fetch(`${API_BASE_URL}/shoes/${id}`, {
    method: 'PATCH',
    headers: { 'Content-Type': 'application/json' },
    credentials: 'include',
    body: JSON.stringify(shoe),
  });

  if (response.status === 404) {
    throw new Error('Shoe not found');
  }

  if (!response.ok) {
    const error = await response.json().catch(() => ({ error: `HTTP error! status: ${response.status}` }));
    throw new Error(error.error || `Failed to update shoe: ${response.status}`);
  }

  return response.json();
}

export async function deleteShoe(id: string): Promise<void> {
  const response = await fetch(`${API_BASE_URL}/shoes/${id}`, {
    method: 'DELETE',
    credentials: 'include',
  });

  if (response.status === 404) {
    throw new Error('Shoe not found');
  }

  if (!response.ok) {
    const error = await response.json().catch(() => ({ error: `HTTP error! status: ${response.status}` }));
    throw new Error(error.error || `Failed to delete shoe: ${response.status}`);
  }
}

export async function getShoeMileage(id: string): Promise<ShoeMileageResponse> {
  const response = await fetch(`${API_BASE_URL}/shoes/${id}/mileage`, {
    method: 'GET',
    headers: { 'Content-Type': 'application/json' },
    credentials: 'include',
  });

  if (response.status === 404) {
    throw new Error('Shoe not found');
  }

  if (!response.ok) {
    throw new Error(`Failed to fetch shoe mileage: ${response.status}`);
  }

  return response.json();
}

export async function getDefaultShoe(): Promise<DefaultShoeResponse> {
  const response = await fetch(`${API_BASE_URL}/settings/default-shoe`, {
    method: 'GET',
    headers: { 'Content-Type': 'application/json' },
    credentials: 'include',
  });

  if (!response.ok) {
    throw new Error(`Failed to fetch default shoe: ${response.status}`);
  }

  return response.json();
}

export async function setDefaultShoe(shoeId: string | null): Promise<DefaultShoeResponse> {
  const response = await fetch(`${API_BASE_URL}/settings/default-shoe`, {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json' },
    credentials: 'include',
    body: JSON.stringify({ defaultShoeId: shoeId }),
  });

  if (!response.ok) {
    const error = await response.json().catch(() => ({ error: `HTTP error! status: ${response.status}` }));
    throw new Error(error.error || `Failed to set default shoe: ${response.status}`);
  }

  return response.json();
}


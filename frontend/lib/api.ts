const API_BASE_URL = '/api';

// Global 401 error handling
let authErrorHandler: (() => void) | null = null;
let isRedirecting = false;

/**
 * Sets the callback function to be called when a 401 error is detected.
 * This allows AuthContext to register a handler for clearing user state.
 */
export function setAuthErrorHandler(handler: (() => void) | null) {
  authErrorHandler = handler;
}

/**
 * Wrapper around fetch that handles 401 (Unauthorized) responses globally.
 * Automatically redirects to login page when session expires.
 */
async function fetchWithAuth(
  input: RequestInfo | URL,
  init?: RequestInit
): Promise<Response> {
  const response = await fetch(input, init);

  // Handle 401 Unauthorized responses
  if (response.status === 401) {
    // Skip redirect for auth endpoints to prevent loops
    let urlString = '';
    if (typeof input === 'string') {
      urlString = input;
    } else if (input instanceof URL) {
      urlString = input.toString();
    } else if (input instanceof Request) {
      urlString = input.url;
    } else {
      // Fallback for other types
      urlString = String(input);
    }
    
    const isAuthEndpoint = urlString.includes('/auth/login') || 
                          urlString.includes('/auth/register') || 
                          urlString.includes('/auth/registration-available');
    
    if (!isAuthEndpoint) {
      // Prevent multiple simultaneous redirects
      if (!isRedirecting) {
        isRedirecting = true;
        
        // Clear authentication state via callback
        if (authErrorHandler) {
          authErrorHandler();
        }
        
        // Only redirect if not already on login page
        if (typeof window !== 'undefined' && window.location.pathname !== '/login') {
          window.location.href = '/login';
        } else {
          // Reset flag if we're already on login page
          isRedirecting = false;
        }
      }
    }
  }

  return response;
}

// Auth interfaces
export interface LoginRequest {
  username: string;
  password: string;
  rememberMe?: boolean;
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
  onboardingCompleted: boolean;
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
  rpe: number | null;
  runType: string | null;
  source: string | null;
  device: string | null;
  healthKitUuid: string | null;
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
  rpe: number | null;
  runType: string | null;
  notes: string | null;
  source: string | null;
  device: string | null;
  healthKitUuid: string | null;
  name: string | null;
  weather: any | null;
  rawGpxData: any | null;
  rawFitData: any | null;
  rawStravaData: any | null;
  rawHealthKitData: any | null;
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

  const response = await fetchWithAuth(`${API_BASE_URL}/workouts/import`, {
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

  const response = await fetchWithAuth(url, {
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
  const response = await fetchWithAuth(`${API_BASE_URL}/workouts/${id}`, {
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

export interface WorkoutTimeSeriesSample {
  elapsedSeconds: number;
  distanceM: number | null;
  heartRateBpm: number | null;
  cadenceRpm: number | null;
  powerWatts: number | null;
  speedMps: number | null;
  gradePercent: number | null;
  elevationM: number | null;
  temperatureC: number | null;
  verticalSpeedMps: number | null;
}

export interface WorkoutTimeSeriesPage {
  items: WorkoutTimeSeriesSample[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
}

/** API default pageSize for GET /workouts/{id}/time-series. */
export const WORKOUT_TIME_SERIES_DEFAULT_PAGE_SIZE = 1000;
/** API maximum pageSize. */
export const WORKOUT_TIME_SERIES_MAX_PAGE_SIZE = 5000;
/**
 * Client fetch cap for overview charts: 4 pages at max page size (20_000 samples).
 * Longer series is truncated; charts still render the samples loaded.
 */
export const WORKOUT_TIME_SERIES_FETCH_CAP = 20_000;

export async function getWorkoutTimeSeriesPage(
  workoutId: string,
  page = 1,
  pageSize = WORKOUT_TIME_SERIES_MAX_PAGE_SIZE
): Promise<WorkoutTimeSeriesPage> {
  const searchParams = new URLSearchParams();
  searchParams.set('page', String(page));
  searchParams.set('pageSize', String(pageSize));

  const response = await fetchWithAuth(
    `${API_BASE_URL}/workouts/${workoutId}/time-series?${searchParams.toString()}`,
    {
      method: 'GET',
      headers: { 'Content-Type': 'application/json' },
      credentials: 'include',
    }
  );

  if (response.status === 404) {
    throw new Error('Workout not found');
  }

  if (!response.ok) {
    throw new Error(`Failed to fetch time series: ${response.status}`);
  }

  return response.json();
}

/**
 * Loads WorkoutTimeSeries for a Workout, following pages until complete
 * or WORKOUT_TIME_SERIES_FETCH_CAP samples.
 */
export async function getWorkoutTimeSeries(
  workoutId: string
): Promise<WorkoutTimeSeriesSample[]> {
  const items: WorkoutTimeSeriesSample[] = [];
  let page = 1;
  let totalPages = 1;

  while (page <= totalPages && items.length < WORKOUT_TIME_SERIES_FETCH_CAP) {
    const remaining = WORKOUT_TIME_SERIES_FETCH_CAP - items.length;
    const pageSize = Math.min(WORKOUT_TIME_SERIES_MAX_PAGE_SIZE, remaining);
    const result = await getWorkoutTimeSeriesPage(workoutId, page, pageSize);
    items.push(...result.items);
    totalPages = result.totalPages;
    if (result.items.length === 0) {
      break;
    }
    page += 1;
  }

  return items;
}

export interface BulkImportResponse {
  totalProcessed: number;
  successful: number;
  updated: number;
  skipped: number;
  errors: number;
  errorDetails: Array<{
    filename: string;
    error: string;
  }>;
}

export const IMPORT_JOB_CHUNK_SIZE = 512 * 1024;
export const IMPORT_JOB_HINT_KEY = 'tempo-import-job-id';

export interface ImportJobStatistics {
  settings: { imported: number; skipped: number; errors: number };
  shoes: { imported: number; skipped: number; errors: number };
  workouts: { imported: number; skipped: number; errors: number };
  routes: { imported: number; skipped: number; errors: number };
  splits: { imported: number; skipped: number; errors: number };
  timeSeries: { imported: number; skipped: number; errors: number };
  media: { imported: number; skipped: number; errors: number };
  bestEfforts: { imported: number; skipped: number; errors: number };
  rawFiles: { imported: number; skipped: number; errors: number };
}

export interface ImportJob {
  id: string;
  kind: string;
  status: 'receiving' | 'queued' | 'running' | 'completed' | 'failed' | string;
  filename: string;
  byteSize: number;
  bytesReceived: number;
  processed: number;
  total: number;
  successful: number;
  skipped: number;
  updated: number;
  errors: number;
  errorDetails: Array<{
    filename: string;
    error: string;
  }>;
  errorMessage: string | null;
  statistics?: ImportJobStatistics | null;
  warnings?: string[] | null;
  errorMessages?: string[] | null;
}

export class ImportJobConflictError extends Error {
  job: ImportJob;

  constructor(job: ImportJob) {
    super('An import is already in progress');
    this.name = 'ImportJobConflictError';
    this.job = job;
  }
}

export function importJobToBulkResponse(job: ImportJob): BulkImportResponse {
  return {
    totalProcessed: job.total,
    successful: job.successful,
    updated: job.updated,
    skipped: job.skipped,
    errors: job.errors,
    errorDetails: job.errorDetails ?? [],
  };
}

const emptyItemStats = () => ({ imported: 0, skipped: 0, errors: 0 });

export function importJobToExportImportResponse(job: ImportJob): ExportImportResponse {
  const stats = job.statistics;
  return {
    success: (job.errorMessages?.length ?? 0) === 0,
    importedAt: new Date().toISOString(),
    statistics: {
      settings: stats?.settings ?? emptyItemStats(),
      shoes: stats?.shoes ?? emptyItemStats(),
      workouts: stats?.workouts ?? emptyItemStats(),
      routes: stats?.routes ?? emptyItemStats(),
      splits: stats?.splits ?? emptyItemStats(),
      timeSeries: stats?.timeSeries ?? emptyItemStats(),
      media: stats?.media ?? emptyItemStats(),
      bestEfforts: stats?.bestEfforts ?? emptyItemStats(),
      rawFiles: stats?.rawFiles ?? emptyItemStats(),
    },
    warnings: job.warnings ?? [],
    errors: job.errorMessages ?? [],
  };
}

/** True when a tempo_export job imported UserSettings (units / HR zones). */
export function importJobHasSettings(job: ImportJob): boolean {
  return (job.statistics?.settings?.imported ?? 0) > 0;
}

export async function exportAllData(): Promise<Blob> {
  const response = await fetchWithAuth(`${API_BASE_URL}/workouts/export`, {
    method: 'POST',
    credentials: 'include',
  });

  if (!response.ok) {
    const error = await response.json().catch(() => ({ error: `HTTP error! status: ${response.status}` }));
    throw new Error(error.error || `Failed to export data: ${response.status}`);
  }

  return response.blob();
}

export interface ExportImportResponse {
  success: boolean;
  importedAt: string;
  statistics: {
    settings: { imported: number; skipped: number; errors: number };
    shoes: { imported: number; skipped: number; errors: number };
    workouts: { imported: number; skipped: number; errors: number };
    routes: { imported: number; skipped: number; errors: number };
    splits: { imported: number; skipped: number; errors: number };
    timeSeries: { imported: number; skipped: number; errors: number };
    media: { imported: number; skipped: number; errors: number };
    bestEfforts: { imported: number; skipped: number; errors: number };
    rawFiles: { imported: number; skipped: number; errors: number };
  };
  warnings: string[];
  errors: string[];
}

export type ImportJobKind = 'strava_bulk' | 'tempo_export';

async function readImportJobResponse(response: Response, fallback: string): Promise<ImportJob> {
  const body = await response.json().catch(() => ({ error: fallback }));
  if (response.status === 409 && body?.id) {
    throw new ImportJobConflictError(body as ImportJob);
  }
  if (!response.ok) {
    throw new Error(body.error || fallback || `HTTP error! status: ${response.status}`);
  }
  return body as ImportJob;
}

export async function createImportJob(
  kind: ImportJobKind,
  filename: string,
  byteSize: number,
  unitPreference?: 'metric' | 'imperial'
): Promise<ImportJob> {
  const response = await fetchWithAuth(`${API_BASE_URL}/workouts/import/jobs`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    credentials: 'include',
    body: JSON.stringify({ kind, filename, byteSize, unitPreference }),
  });
  return readImportJobResponse(response, 'Failed to create import job');
}

export async function putImportJobChunk(
  jobId: string,
  index: number,
  total: number,
  chunk: Blob
): Promise<ImportJob> {
  const response = await fetchWithAuth(
    `${API_BASE_URL}/workouts/import/jobs/${jobId}/chunks/${index}?total=${total}`,
    {
      method: 'PUT',
      headers: { 'Content-Type': 'application/octet-stream' },
      credentials: 'include',
      body: chunk,
    }
  );
  return readImportJobResponse(response, 'Failed to upload import chunk');
}

export async function completeImportJob(jobId: string): Promise<ImportJob> {
  const response = await fetchWithAuth(`${API_BASE_URL}/workouts/import/jobs/${jobId}/complete`, {
    method: 'POST',
    credentials: 'include',
  });
  return readImportJobResponse(response, 'Failed to complete import upload');
}

export async function uploadImportJobChunks(
  kind: ImportJobKind,
  zipFile: File,
  unitPreference: 'metric' | 'imperial' | undefined,
  onProgress: (bytesReceived: number, byteSize: number) => void,
  onJob?: (job: ImportJob) => void
): Promise<ImportJob> {
  const created = await createImportJob(kind, zipFile.name, zipFile.size, unitPreference);
  onJob?.(created);
  const total = Math.max(1, Math.ceil(zipFile.size / IMPORT_JOB_CHUNK_SIZE));
  onProgress(0, zipFile.size);

  for (let index = 0; index < total; index++) {
    const start = index * IMPORT_JOB_CHUNK_SIZE;
    const end = Math.min(start + IMPORT_JOB_CHUNK_SIZE, zipFile.size);
    const updated = await putImportJobChunk(created.id, index, total, zipFile.slice(start, end));
    onProgress(updated.bytesReceived, zipFile.size);
  }

  return completeImportJob(created.id);
}

export async function importStravaExportChunked(
  zipFile: File,
  unitPreference: 'metric' | 'imperial' | undefined,
  onProgress: (bytesReceived: number, byteSize: number) => void,
  onJob?: (job: ImportJob) => void
): Promise<ImportJob> {
  return uploadImportJobChunks('strava_bulk', zipFile, unitPreference, onProgress, onJob);
}

export async function importTempoExportChunked(
  zipFile: File,
  onProgress: (bytesReceived: number, byteSize: number) => void,
  onJob?: (job: ImportJob) => void
): Promise<ImportJob> {
  return uploadImportJobChunks('tempo_export', zipFile, undefined, onProgress, onJob);
}

export async function getImportJob(jobId: string): Promise<ImportJob> {
  const response = await fetchWithAuth(`${API_BASE_URL}/workouts/import/jobs/${jobId}`, {
    method: 'GET',
    credentials: 'include',
  });

  if (!response.ok) {
    const error = await response.json().catch(() => ({ error: 'Failed to fetch import job' }));
    throw new Error(error.error || `HTTP error! status: ${response.status}`);
  }

  return response.json();
}

export async function getCurrentImportJob(): Promise<ImportJob | null> {
  const response = await fetchWithAuth(`${API_BASE_URL}/workouts/import/jobs/current`, {
    method: 'GET',
    credentials: 'include',
  });

  if (response.status === 204) {
    return null;
  }

  if (!response.ok) {
    const error = await response.json().catch(() => ({ error: 'Failed to fetch current import job' }));
    throw new Error(error.error || `HTTP error! status: ${response.status}`);
  }

  return response.json();
}

export async function cancelImportJob(jobId: string): Promise<ImportJob> {
  const response = await fetchWithAuth(`${API_BASE_URL}/workouts/import/jobs/${jobId}`, {
    method: 'DELETE',
    credentials: 'include',
  });
  return readImportJobResponse(response, 'Failed to cancel import job');
}

export async function getWorkoutMedia(workoutId: string): Promise<WorkoutMedia[]> {
  const response = await fetchWithAuth(`${API_BASE_URL}/workouts/${workoutId}/media`, {
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
  const response = await fetchWithAuth(`${API_BASE_URL}/workouts/${workoutId}/media/${mediaId}`, {
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

  const response = await fetchWithAuth(`${API_BASE_URL}/workouts/${workoutId}/media`, {
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

/** Non-nullable week-over-week metric block (API uses int/long/double; JSON/TS are all number). */
export interface WeeklyRecapNumericMetric {
  current: number;
  previous: number;
  trailingAvg: number;
  deltaVsPrevious: number;
}

export interface WeeklyRecapMetricNullableDouble {
  current: number | null;
  previous: number | null;
  trailingAvg: number | null;
  deltaVsPrevious: number | null;
}

export interface WeeklyRecapResponse {
  weekStart: string;
  weekEnd: string;
  referenceDate: string;
  timezoneOffsetMinutes: number | null;
  currentWeekIsPartial: boolean;
  generatedAtUtc: string;
  metrics: {
    runs: WeeklyRecapNumericMetric;
    distanceM: WeeklyRecapNumericMetric;
    durationS: WeeklyRecapNumericMetric;
    elevationGainM: WeeklyRecapNumericMetric;
    relativeEffortSum: WeeklyRecapNumericMetric;
    easyRunAvgHeartRateBpm: WeeklyRecapMetricNullableDouble;
  };
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
  const url = `${API_BASE_URL}/stats/weekly${queryString ? `?${queryString}` : ''}`;

  const response = await fetchWithAuth(url, {
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
  const url = `${API_BASE_URL}/stats/relative-effort${queryString ? `?${queryString}` : ''}`;

  const response = await fetchWithAuth(url, {
    method: 'GET',
    headers: { 'Content-Type': 'application/json' },
    credentials: 'include',
  });

  if (!response.ok) {
    throw new Error(`Failed to fetch relative effort stats: ${response.status}`);
  }

  return response.json();
}

export async function getWeeklyRecap(
  timezoneOffsetMinutes?: number,
  referenceDate?: string
): Promise<WeeklyRecapResponse> {
  const searchParams = new URLSearchParams();
  if (timezoneOffsetMinutes !== undefined) {
    searchParams.set('timezoneOffsetMinutes', timezoneOffsetMinutes.toString());
  }
  if (referenceDate) {
    searchParams.set('referenceDate', referenceDate);
  }

  const queryString = searchParams.toString();
  const url = `${API_BASE_URL}/stats/weekly-recap${queryString ? `?${queryString}` : ''}`;

  const response = await fetchWithAuth(url, {
    method: 'GET',
    headers: { 'Content-Type': 'application/json' },
    credentials: 'include',
  });

  if (!response.ok) {
    throw new Error(`Failed to fetch weekly recap: ${response.status}`);
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
  const url = `${API_BASE_URL}/stats/best-efforts`;

  const response = await fetchWithAuth(url, {
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
  const url = `${API_BASE_URL}/stats/best-efforts/recalculate`;

  const response = await fetchWithAuth(url, {
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
  const url = `${API_BASE_URL}/stats/yearly${queryString ? `?${queryString}` : ''}`;

  const response = await fetchWithAuth(url, {
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
  const url = `${API_BASE_URL}/stats/yearly-weekly${queryString ? `?${queryString}` : ''}`;

  const response = await fetchWithAuth(url, {
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
  const url = `${API_BASE_URL}/stats/available-periods${queryString ? `?${queryString}` : ''}`;

  const response = await fetchWithAuth(url, {
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
  const url = `${API_BASE_URL}/stats/available-years`;

  const response = await fetchWithAuth(url, {
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
  rpe?: number | null;
}

export interface UpdateWorkoutResponse {
  id: string;
  runType: string | null;
  notes: string | null;
  name: string | null;
  shoeId: string | null;
  rpe: number | null;
}

export async function updateWorkout(
  id: string,
  updates: UpdateWorkoutRequest
): Promise<UpdateWorkoutResponse> {
  const response = await fetchWithAuth(`${API_BASE_URL}/workouts/${id}`, {
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
  const response = await fetchWithAuth(`${API_BASE_URL}/workouts/${id}`, {
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
  const response = await fetchWithAuth(`${API_BASE_URL}/settings/heart-rate-zones`, {
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
  const response = await fetchWithAuth(`${API_BASE_URL}/settings/heart-rate-zones`, {
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
  const response = await fetchWithAuth(`${API_BASE_URL}/settings/heart-rate-zones/update-with-recalc`, {
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
  const response = await fetchWithAuth(`${API_BASE_URL}/workouts/recalculate-relative-effort/count`, {
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
  const response = await fetchWithAuth(`${API_BASE_URL}/workouts/recalculate-relative-effort`, {
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

export async function getCartoBasemaps(): Promise<{ apiKey: string | null }> {
  const response = await fetchWithAuth(`${API_BASE_URL}/settings/carto-basemaps`, {
    method: 'GET',
    headers: { 'Content-Type': 'application/json' },
    credentials: 'include',
  });

  if (!response.ok) {
    throw new Error(`Failed to get CARTO basemaps configuration: ${response.status}`);
  }

  return response.json();
}

export async function getUnitPreference(): Promise<{ unitPreference: 'metric' | 'imperial' }> {
  const response = await fetchWithAuth(`${API_BASE_URL}/settings/unit-preference`, {
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
  const response = await fetchWithAuth(`${API_BASE_URL}/settings/unit-preference`, {
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
  const response = await fetchWithAuth(`${API_BASE_URL}/workouts/recalculate-splits/count`, {
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
  const response = await fetchWithAuth(`${API_BASE_URL}/workouts/recalculate-splits`, {
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
  const response = await fetchWithAuth(`${API_BASE_URL}/workouts/${workoutId}/recalculate-splits`, {
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
  const response = await fetchWithAuth(`${API_BASE_URL}/workouts/${workoutId}/crop`, {
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
  const response = await fetchWithAuth(`${API_BASE_URL}/version`, {
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
export async function login(username: string, password: string, rememberMe?: boolean): Promise<AuthResponse> {
  const response = await fetch(`${API_BASE_URL}/auth/login`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    credentials: 'include',
    body: JSON.stringify({ username, password, rememberMe }),
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
  const response = await fetchWithAuth(`${API_BASE_URL}/auth/me`, {
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

export async function completeOnboarding(): Promise<UserInfo> {
  const response = await fetchWithAuth(`${API_BASE_URL}/auth/onboarding/complete`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    credentials: 'include',
  });

  if (response.status === 401) {
    throw new Error('Not authenticated');
  }

  if (!response.ok) {
    throw new Error(`Failed to complete onboarding: ${response.status}`);
  }

  return response.json();
}

export async function logout(): Promise<void> {
  const response = await fetchWithAuth(`${API_BASE_URL}/auth/logout`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    credentials: 'include',
  });

  if (!response.ok) {
    throw new Error(`Logout failed: ${response.status}`);
  }
}

export async function changePassword(
  currentPassword: string,
  newPassword: string
): Promise<{ message: string }> {
  const response = await fetchWithAuth(`${API_BASE_URL}/auth/change-password`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    credentials: 'include',
    body: JSON.stringify({ currentPassword, newPassword }),
  });

  if (!response.ok) {
    const error = await response.json().catch(() => ({ error: 'Password change failed' }));
    throw new Error(
      typeof error.error === 'string' ? error.error : `Password change failed: ${response.status}`
    );
  }

  return response.json();
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
  isRetired: boolean;
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
  isRetired?: boolean;
}

export interface UpdateShoeRequest {
  brand?: string;
  model?: string;
  initialMileageM?: number | null;
  isRetired?: boolean;
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

export type ShoesListStatus = 'active' | 'retired' | 'all';

// Shoe API functions
export async function getShoes(params?: { status?: ShoesListStatus }): Promise<Shoe[]> {
  const status = params?.status ?? 'active';
  const qs = new URLSearchParams({ status });
  const response = await fetchWithAuth(`${API_BASE_URL}/shoes?${qs.toString()}`, {
    method: 'GET',
    headers: { 'Content-Type': 'application/json' },
    credentials: 'include',
  });

  if (!response.ok) {
    const err = await response.json().catch(() => null);
    const msg = err && typeof err === 'object' && 'error' in err ? String((err as { error: string }).error) : `Failed to fetch shoes: ${response.status}`;
    throw new Error(msg);
  }

  return response.json();
}

export async function createShoe(shoe: CreateShoeRequest): Promise<Shoe> {
  const response = await fetchWithAuth(`${API_BASE_URL}/shoes`, {
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
  const response = await fetchWithAuth(`${API_BASE_URL}/shoes/${id}`, {
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
  const response = await fetchWithAuth(`${API_BASE_URL}/shoes/${id}`, {
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
  const response = await fetchWithAuth(`${API_BASE_URL}/shoes/${id}/mileage`, {
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
  const response = await fetchWithAuth(`${API_BASE_URL}/settings/default-shoe`, {
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
  const response = await fetchWithAuth(`${API_BASE_URL}/settings/default-shoe`, {
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

// Similar Routes interfaces and functions
export interface SimilarRoute {
  workoutId: string;
  startedAt: string;
  durationS: number;
  distanceM: number;
  avgPaceS: number;
  similarityScore?: number;
  timeDifferenceS?: number; // Negative = faster, Positive = slower
  paceDifferenceS?: number; // Negative = faster pace
  relativeEffort?: number | null;
  elevGainM?: number | null;
}

export async function getSimilarRoutes(workoutId: string, maxResults?: number): Promise<SimilarRoute[]> {
  const searchParams = new URLSearchParams();
  if (maxResults !== undefined) {
    searchParams.set('maxResults', maxResults.toString());
  }

  const queryString = searchParams.toString();
  const url = `${API_BASE_URL}/workouts/${workoutId}/similar-routes${queryString ? `?${queryString}` : ''}`;

  const response = await fetchWithAuth(url, {
    method: 'GET',
    headers: { 'Content-Type': 'application/json' },
    credentials: 'include',
  });

  if (response.status === 404) {
    throw new Error('Workout not found');
  }

  if (response.status === 400) {
    const error = await response.json().catch(() => ({ error: 'Workout has no route data' }));
    throw new Error(error.error || 'Workout has no route data');
  }

  if (!response.ok) {
    throw new Error(`Failed to fetch similar routes: ${response.status}`);
  }

  return response.json();
}


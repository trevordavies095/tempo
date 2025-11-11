const API_BASE_URL = process.env.NEXT_PUBLIC_API_URL || 'http://localhost:5001';

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
  runType: string | null;
  source: string | null;
  hasRoute: boolean;
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
}

export interface WorkoutDetail {
  id: string;
  startedAt: string;
  durationS: number;
  distanceM: number;
  avgPaceS: number;
  elevGainM: number | null;
  runType: string | null;
  notes: string | null;
  source: string | null;
  weather: any | null;
  createdAt: string;
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

export async function importGpxFile(file: File): Promise<WorkoutImportResponse> {
  const formData = new FormData();
  formData.append('file', file);

  const response = await fetch(`${API_BASE_URL}/workouts/import`, {
    method: 'POST',
    mode: 'cors',
    credentials: 'omit',
    body: formData,
  });

  if (!response.ok) {
    const error = await response.json().catch(() => ({ error: 'Failed to import GPX file' }));
    throw new Error(error.error || `HTTP error! status: ${response.status}`);
  }

  return response.json();
}

export async function getWorkouts(
  params?: WorkoutsListParams
): Promise<WorkoutsListResponse> {
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

  const queryString = searchParams.toString();
  const url = `${API_BASE_URL}/workouts${queryString ? `?${queryString}` : ''}`;

  const response = await fetch(url, {
    method: 'GET',
    headers: { 'Content-Type': 'application/json' },
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

export async function importBulkStravaExport(zipFile: File): Promise<BulkImportResponse> {
  const formData = new FormData();
  formData.append('file', zipFile);

  const response = await fetch(`${API_BASE_URL}/workouts/import/bulk`, {
    method: 'POST',
    mode: 'cors',
    credentials: 'omit',
    body: formData,
  });

  if (!response.ok) {
    const error = await response.json().catch(() => ({ error: 'Failed to import Strava export' }));
    throw new Error(error.error || `HTTP error! status: ${response.status}`);
  }

  return response.json();
}


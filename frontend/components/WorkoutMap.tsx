'use client';

import { useEffect, useMemo, useRef, useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import L from 'leaflet';
import 'leaflet/dist/leaflet.css';
import { getCartoBasemaps } from '@/lib/api';
import { cartoTilesForAppearance } from '@/lib/cartoTiles';
import {
  highlightFromRouteDistance,
  routeDistanceFromElapsed,
  type WorkoutHighlight,
  type WorkoutSplit,
} from '@/lib/workoutHighlight';

// Fix for default marker icons in Next.js
if (typeof window !== 'undefined') {
  delete (L.Icon.Default.prototype as any)._getIconUrl;
  L.Icon.Default.mergeOptions({
    iconRetinaUrl: 'https://cdnjs.cloudflare.com/ajax/libs/leaflet/1.9.4/images/marker-icon-2x.png',
    iconUrl: 'https://cdnjs.cloudflare.com/ajax/libs/leaflet/1.9.4/images/marker-icon.png',
    shadowUrl: 'https://cdnjs.cloudflare.com/ajax/libs/leaflet/1.9.4/images/marker-shadow.png',
  });
}

const CARTO_ATTRIBUTION =
  '&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a> contributors &copy; <a href="https://carto.com/attributions">CARTO</a>';

const TILE_OPTIONS: L.TileLayerOptions = {
  attribution: CARTO_ATTRIBUTION,
  subdomains: 'abcd',
  maxZoom: 20,
};

/** Light-mode route stroke: `--ink` flips in `.dark`, so keep a fixed dark stroke. */
const LIGHT_POLYLINE = '#1c1917';

interface RouteGeoJson {
  type: string;
  coordinates: [number, number][];
}

interface WorkoutMapProps {
  route: RouteGeoJson | null;
  workoutId?: string;
  splits?: WorkoutSplit[];
  hoveredSplitIdx?: number | null;
  highlightElapsedSeconds?: number | null;
  workoutDistanceM?: number;
  workoutDurationS?: number;
  onHighlightFromMap?: (highlight: WorkoutHighlight | null) => void;
  height?: string; // Optional height class (e.g., 'h-48', 'h-64')
  interactive?: boolean; // Whether the map should be interactive (default: true)
}

function readCssVar(name: string, fallback: string): string {
  if (typeof window === 'undefined') {
    return fallback;
  }
  const value = getComputedStyle(document.documentElement)
    .getPropertyValue(name)
    .trim();
  return value || fallback;
}

function mapStrokeColors(isDark: boolean) {
  return {
    polyline: isDark ? readCssVar('--volt', '#e8ff00') : LIGHT_POLYLINE,
    highlight: readCssVar('--danger', isDark ? '#f07171' : '#e05656'),
  };
}

function useDocumentDark(): boolean {
  const [isDark, setIsDark] = useState(() =>
    typeof document !== 'undefined'
      ? document.documentElement.classList.contains('dark')
      : false
  );

  useEffect(() => {
    const root = document.documentElement;
    const sync = () => setIsDark(root.classList.contains('dark'));
    sync();
    const observer = new MutationObserver(sync);
    observer.observe(root, { attributes: true, attributeFilter: ['class'] });
    return () => observer.disconnect();
  }, []);

  return isDark;
}

// Haversine distance calculation (same as backend)
function haversineDistance(lat1: number, lon1: number, lat2: number, lon2: number): number {
  const R = 6371000; // Earth radius in meters
  const toRadians = (degrees: number) => degrees * Math.PI / 180.0;
  
  const dLat = toRadians(lat2 - lat1);
  const dLon = toRadians(lon2 - lon1);
  
  const a = Math.sin(dLat / 2) * Math.sin(dLat / 2) +
            Math.cos(toRadians(lat1)) * Math.cos(toRadians(lat2)) *
            Math.sin(dLon / 2) * Math.sin(dLon / 2);
  
  const c = 2 * Math.atan2(Math.sqrt(a), Math.sqrt(1 - a));
  return R * c;
}

interface SplitSegment {
  splitIdx: number;
  startIdx: number;
  endIdx: number;
}

// Calculate which route coordinate indices correspond to each split
function calculateSplitSegments(
  coordinates: [number, number][],
  splits: WorkoutSplit[]
): SplitSegment[] {
  if (!coordinates || coordinates.length === 0 || !splits || splits.length === 0) {
    return [];
  }

  const segments: SplitSegment[] = [];
  let accumulatedDistance = 0.0;
  let splitStartDistance = 0.0;
  let splitStartIndex = 0;
  let currentSplitIdx = 0;

  // Sort splits by idx to ensure correct order
  const sortedSplits = [...splits].sort((a, b) => a.idx - b.idx);

  for (let i = 1; i < coordinates.length && currentSplitIdx < sortedSplits.length; i++) {
    const [lon1, lat1] = coordinates[i - 1];
    const [lon2, lat2] = coordinates[i];
    
    const segmentDistance = haversineDistance(lat1, lon1, lat2, lon2);
    accumulatedDistance += segmentDistance;

    const currentSplit = sortedSplits[currentSplitIdx];
    const splitTargetDistance = splitStartDistance + currentSplit.distanceM;

    if (accumulatedDistance >= splitTargetDistance) {
      // This split ends at or before this coordinate
      segments.push({
        splitIdx: currentSplit.idx,
        startIdx: splitStartIndex,
        endIdx: i,
      });

      // Move to next split
      splitStartDistance = accumulatedDistance;
      splitStartIndex = i;
      currentSplitIdx++;
    }
  }

  // Handle final split if there's remaining distance
  if (currentSplitIdx < sortedSplits.length) {
    const finalSplit = sortedSplits[currentSplitIdx];
    segments.push({
      splitIdx: finalSplit.idx,
      startIdx: splitStartIndex,
      endIdx: coordinates.length - 1,
    });
  }

  return segments;
}

const MAP_HIT_PX = 24;

function pointAtDistance(
  coordinates: [number, number][],
  targetM: number
): [number, number] | null {
  if (coordinates.length === 0) {
    return null;
  }
  if (coordinates.length === 1 || targetM <= 0) {
    const [lon, lat] = coordinates[0];
    return [lat, lon];
  }

  let accumulated = 0;
  for (let i = 1; i < coordinates.length; i++) {
    const [lon1, lat1] = coordinates[i - 1];
    const [lon2, lat2] = coordinates[i];
    const segment = haversineDistance(lat1, lon1, lat2, lon2);
    if (accumulated + segment >= targetM || i === coordinates.length - 1) {
      const t =
        segment > 0
          ? Math.min(1, Math.max(0, (targetM - accumulated) / segment))
          : 1;
      return [lat1 + (lat2 - lat1) * t, lon1 + (lon2 - lon1) * t];
    }
    accumulated += segment;
  }

  const [lon, lat] = coordinates[coordinates.length - 1];
  return [lat, lon];
}

function closestPointOnRoute(
  latlng: L.LatLng,
  leafletCoordinates: [number, number][]
): { lat: number; lon: number; distanceAlongM: number } | null {
  if (leafletCoordinates.length === 0) {
    return null;
  }

  let best = {
    lat: leafletCoordinates[0][0],
    lon: leafletCoordinates[0][1],
    distanceAlongM: 0,
    distSq: Number.POSITIVE_INFINITY,
  };
  let along = 0;

  for (let i = 1; i < leafletCoordinates.length; i++) {
    const [lat1, lon1] = leafletCoordinates[i - 1];
    const [lat2, lon2] = leafletCoordinates[i];
    const dx = lat2 - lat1;
    const dy = lon2 - lon1;
    const lenSq = dx * dx + dy * dy;
    const t =
      lenSq === 0
        ? 0
        : Math.min(
            1,
            Math.max(
              0,
              ((latlng.lat - lat1) * dx + (latlng.lng - lon1) * dy) / lenSq
            )
          );
    const lat = lat1 + dx * t;
    const lon = lon1 + dy * t;
    const dLat = latlng.lat - lat;
    const dLon = latlng.lng - lon;
    const distSq = dLat * dLat + dLon * dLon;
    const alongHere = along + t * haversineDistance(lat1, lon1, lat2, lon2);

    if (distSq < best.distSq) {
      best = { lat, lon, distanceAlongM: alongHere, distSq };
    }

    along += haversineDistance(lat1, lon1, lat2, lon2);
  }

  return {
    lat: best.lat,
    lon: best.lon,
    distanceAlongM: best.distanceAlongM,
  };
}

export default function WorkoutMap({
  route,
  workoutId,
  splits,
  hoveredSplitIdx,
  highlightElapsedSeconds = null,
  workoutDistanceM,
  workoutDurationS,
  onHighlightFromMap,
  height = 'h-64',
  interactive = true,
}: WorkoutMapProps) {
  const isDark = useDocumentDark();
  const { data: cartoBasemaps, isPending: cartoBasemapsPending } = useQuery({
    queryKey: ['carto-basemaps'],
    queryFn: getCartoBasemaps,
    staleTime: Infinity,
    retry: 1,
  });
  const cartoApiKey = cartoBasemaps?.apiKey ?? null;
  // Ref to store the Leaflet map instance
  const mapRef = useRef<L.Map | null>(null);
  // Ref to container div element
  const containerRef = useRef<HTMLDivElement>(null);
  const tileLayerRef = useRef<L.TileLayer | null>(null);
  // Ref to store polyline instance for cleanup
  const polylineRef = useRef<L.Polyline | null>(null);
  // Ref to store highlighted polyline instance for cleanup
  const highlightedPolylineRef = useRef<L.Polyline | null>(null);
  const highlightMarkerRef = useRef<L.CircleMarker | null>(null);
  const onHighlightFromMapRef = useRef(onHighlightFromMap);
  const splitsRef = useRef(splits);
  const totalsRef = useRef({ workoutDistanceM, workoutDurationS });

  const leafletCoordinates = useMemo(() => {
    if (!route || !route.coordinates || route.coordinates.length === 0) {
      return [];
    }
    return route.coordinates.map(([lon, lat]) => [lat, lon] as [number, number]);
  }, [route]);

  const leafletCoordinatesRef = useRef(leafletCoordinates);

  onHighlightFromMapRef.current = onHighlightFromMap;
  splitsRef.current = splits;
  totalsRef.current = { workoutDistanceM, workoutDurationS };
  leafletCoordinatesRef.current = leafletCoordinates;

  // Calculate bounds from coordinates
  const bounds = useMemo(() => {
    if (leafletCoordinates.length === 0) {
      return null;
    }

    const lats = leafletCoordinates.map(([lat]) => lat);
    const lons = leafletCoordinates.map(([, lon]) => lon);

    const minLat = Math.min(...lats);
    const maxLat = Math.max(...lats);
    const minLon = Math.min(...lons);
    const maxLon = Math.max(...lons);

    return L.latLngBounds(
      [minLat, minLon],
      [maxLat, maxLon]
    );
  }, [leafletCoordinates]);

  // Calculate center point for initial map view
  const center = useMemo(() => {
    if (leafletCoordinates.length === 0) {
      return [0, 0] as [number, number];
    }

    const lats = leafletCoordinates.map(([lat]) => lat);
    const lons = leafletCoordinates.map(([, lon]) => lon);

    const avgLat = (Math.min(...lats) + Math.max(...lats)) / 2;
    const avgLon = (Math.min(...lons) + Math.max(...lons)) / 2;

    return [avgLat, avgLon] as [number, number];
  }, [leafletCoordinates]);

  // Effect to create and manage the Leaflet map
  useEffect(() => {
    if (cartoBasemapsPending) {
      return;
    }

    if (!containerRef.current || !route || leafletCoordinates.length === 0) {
      return;
    }

    const container = containerRef.current;

    // Clean up existing map if it exists
    if (mapRef.current) {
      mapRef.current.remove();
      mapRef.current = null;
    }

    // Ensure container is clean (remove any lingering Leaflet references)
    if ((container as any)._leaflet_id) {
      delete (container as any)._leaflet_id;
    }
    if ((container as any)._leaflet) {
      delete (container as any)._leaflet;
    }

    // Create new map instance
    const map = L.map(container, {
      center: center,
      zoom: 13,
      scrollWheelZoom: interactive,
      dragging: interactive,
      doubleClickZoom: interactive,
      touchZoom: interactive,
      boxZoom: interactive,
      keyboard: interactive,
    });

    const isDarkMode = document.documentElement.classList.contains('dark');
    const tileUrl = cartoTilesForAppearance(isDarkMode, cartoApiKey);
    const tileLayer = L.tileLayer(tileUrl, TILE_OPTIONS).addTo(map);

    const { polyline: polylineColor } = mapStrokeColors(isDarkMode);
    const polyline = L.polyline(leafletCoordinates, {
      color: polylineColor,
      weight: 4,
      opacity: 0.9,
    }).addTo(map);

    const emitFromLatLng = (latlng: L.LatLng) => {
      const emit = onHighlightFromMapRef.current;
      if (!emit) {
        return;
      }
      const closest = closestPointOnRoute(latlng, leafletCoordinatesRef.current);
      if (!closest) {
        emit(null);
        return;
      }
      const pixelDist = map
        .latLngToLayerPoint(latlng)
        .distanceTo(map.latLngToLayerPoint(L.latLng(closest.lat, closest.lon)));
      if (pixelDist > MAP_HIT_PX) {
        emit(null);
        return;
      }
      const totals =
        totalsRef.current.workoutDistanceM != null &&
        totalsRef.current.workoutDurationS != null
          ? {
              totalDistanceM: totalsRef.current.workoutDistanceM,
              totalDurationS: totalsRef.current.workoutDurationS,
            }
          : undefined;
      const next = highlightFromRouteDistance(
        splitsRef.current ?? [],
        closest.distanceAlongM,
        totals
      );
      emit({
        ...next,
        elapsedSeconds:
          next.elapsedSeconds == null
            ? null
            : Math.round(next.elapsedSeconds),
      });
    };

    if (interactive) {
      map.on('mousemove', (event: L.LeafletMouseEvent) => {
        emitFromLatLng(event.latlng);
      });
      map.on('click', (event: L.LeafletMouseEvent) => {
        emitFromLatLng(event.latlng);
      });
      map.on('mouseout', () => {
        onHighlightFromMapRef.current?.(null);
      });
    }

    // Fit bounds if available
    if (bounds) {
      map.fitBounds(bounds, { padding: [20, 20] });
    }

    // Store references
    mapRef.current = map;
    tileLayerRef.current = tileLayer;
    polylineRef.current = polyline;

    // Cleanup function
    return () => {
      const mapToCleanup = mapRef.current;
      if (!mapToCleanup) {
        return;
      }

      // Remove layers before removing the map
      if (highlightedPolylineRef.current) {
        try {
          mapToCleanup.removeLayer(highlightedPolylineRef.current);
        } catch (e) {
          // Ignore errors if layer was already removed
        }
        highlightedPolylineRef.current = null;
      }
      if (highlightMarkerRef.current) {
        try {
          mapToCleanup.removeLayer(highlightMarkerRef.current);
        } catch (e) {
          // Ignore errors if layer was already removed
        }
        highlightMarkerRef.current = null;
      }
      if (polylineRef.current) {
        try {
          mapToCleanup.removeLayer(polylineRef.current);
        } catch (e) {
          // Ignore errors if layer was already removed
        }
        polylineRef.current = null;
      }
      tileLayerRef.current = null;
      
      // Remove the map
      try {
        mapToCleanup.remove();
      } catch (e) {
        // Ignore errors if map was already removed
      }
      mapRef.current = null;

      // Clean up container references
      if (container && (container as any)._leaflet_id) {
        delete (container as any)._leaflet_id;
      }
      if (container && (container as any)._leaflet) {
        delete (container as any)._leaflet;
      }
    };
  }, [workoutId, center, bounds, leafletCoordinates, route, interactive, cartoBasemapsPending, cartoApiKey]);

  useEffect(() => {
    if (cartoBasemapsPending) {
      return;
    }

    const tileUrl = cartoTilesForAppearance(isDark, cartoApiKey);
    tileLayerRef.current?.setUrl(tileUrl);

    const colors = mapStrokeColors(isDark);
    polylineRef.current?.setStyle({ color: colors.polyline });
    highlightedPolylineRef.current?.setStyle({ color: colors.highlight });
    highlightMarkerRef.current?.setStyle({
      color: colors.highlight,
      fillColor: colors.highlight,
    });
  }, [isDark, cartoBasemapsPending, cartoApiKey]);

  // Effect to handle highlighted split segment
  useEffect(() => {
    if (!mapRef.current || !route || !splits || splits.length === 0 || hoveredSplitIdx === null || hoveredSplitIdx === undefined) {
      // Remove highlighted polyline if no hover or invalid data
      if (highlightedPolylineRef.current && mapRef.current) {
        mapRef.current.removeLayer(highlightedPolylineRef.current);
        highlightedPolylineRef.current = null;
      }
      return;
    }

    const map = mapRef.current;

    // Calculate split segments
    const segments = calculateSplitSegments(route.coordinates, splits);
    const segment = segments.find(s => s.splitIdx === hoveredSplitIdx);

    if (!segment) {
      // Remove highlighted polyline if segment not found
      if (highlightedPolylineRef.current) {
        map.removeLayer(highlightedPolylineRef.current);
        highlightedPolylineRef.current = null;
      }
      return;
    }

    // Remove existing highlighted polyline if it exists
    if (highlightedPolylineRef.current) {
      map.removeLayer(highlightedPolylineRef.current);
      highlightedPolylineRef.current = null;
    }

    // Extract coordinates for the highlighted segment
    const segmentCoordinates = route.coordinates
      .slice(segment.startIdx, segment.endIdx + 1)
      .map(([lon, lat]) => [lat, lon] as [number, number]);

    if (segmentCoordinates.length < 2) {
      return;
    }

    const { highlight } = mapStrokeColors(
      document.documentElement.classList.contains('dark')
    );
    const highlightedPolyline = L.polyline(segmentCoordinates, {
      color: highlight,
      weight: 7,
      opacity: 1,
      interactive: false,
    }).addTo(map);

    highlightedPolylineRef.current = highlightedPolyline;
  }, [hoveredSplitIdx, route, splits]);

  useEffect(() => {
    if (!mapRef.current || !route || highlightElapsedSeconds == null) {
      if (highlightMarkerRef.current && mapRef.current) {
        mapRef.current.removeLayer(highlightMarkerRef.current);
        highlightMarkerRef.current = null;
      }
      return;
    }

    const totals =
      workoutDistanceM != null && workoutDurationS != null
        ? { totalDistanceM: workoutDistanceM, totalDurationS: workoutDurationS }
        : undefined;
    const distanceM = routeDistanceFromElapsed(
      splits ?? [],
      highlightElapsedSeconds,
      totals
    );
    const point =
      distanceM == null ? null : pointAtDistance(route.coordinates, distanceM);

    if (!point) {
      if (highlightMarkerRef.current) {
        mapRef.current.removeLayer(highlightMarkerRef.current);
        highlightMarkerRef.current = null;
      }
      return;
    }

    const { highlight } = mapStrokeColors(
      document.documentElement.classList.contains('dark')
    );

    if (highlightMarkerRef.current) {
      highlightMarkerRef.current.setLatLng(point);
      highlightMarkerRef.current.setStyle({
        color: highlight,
        fillColor: highlight,
      });
      return;
    }

    highlightMarkerRef.current = L.circleMarker(point, {
      radius: 7,
      color: highlight,
      fillColor: highlight,
      fillOpacity: 1,
      weight: 2,
      interactive: false,
    }).addTo(mapRef.current);
  }, [
    highlightElapsedSeconds,
    route,
    splits,
    workoutDistanceM,
    workoutDurationS,
  ]);

  if (!route || !route.coordinates || route.coordinates.length === 0) {
    return (
      <div className={`flex items-center justify-center ${height} bg-canvas rounded-tempo border border-border`}>
        <p className="text-muted">No route data available</p>
      </div>
    );
  }

  return (
    <div
      ref={containerRef}
      className={`w-full ${height} rounded-tempo overflow-hidden border border-border`}
      style={{ position: 'relative', isolation: 'isolate' }}
    />
  );
}

'use client';

import { useEffect, useMemo, useRef } from 'react';
import L from 'leaflet';
import 'leaflet/dist/leaflet.css';

// Fix for default marker icons in Next.js
if (typeof window !== 'undefined') {
  delete (L.Icon.Default.prototype as any)._getIconUrl;
  L.Icon.Default.mergeOptions({
    iconRetinaUrl: 'https://cdnjs.cloudflare.com/ajax/libs/leaflet/1.9.4/images/marker-icon-2x.png',
    iconUrl: 'https://cdnjs.cloudflare.com/ajax/libs/leaflet/1.9.4/images/marker-icon.png',
    shadowUrl: 'https://cdnjs.cloudflare.com/ajax/libs/leaflet/1.9.4/images/marker-shadow.png',
  });
}

interface RouteGeoJson {
  type: string;
  coordinates: [number, number][];
}

interface Split {
  idx: number;
  distanceM: number;
  durationS: number;
  paceS: number;
}

interface WorkoutMapProps {
  route: RouteGeoJson | null;
  workoutId?: string;
  splits?: Split[];
  hoveredSplitIdx?: number | null;
  height?: string; // Optional height class (e.g., 'h-48', 'h-64')
  interactive?: boolean; // Whether the map should be interactive (default: true)
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
  splits: Split[]
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

export default function WorkoutMap({ route, workoutId, splits, hoveredSplitIdx, height = 'h-64', interactive = true }: WorkoutMapProps) {
  // Ref to store the Leaflet map instance
  const mapRef = useRef<L.Map | null>(null);
  // Ref to container div element
  const containerRef = useRef<HTMLDivElement>(null);
  // Ref to store polyline instance for cleanup
  const polylineRef = useRef<L.Polyline | null>(null);
  // Ref to store highlighted polyline instance for cleanup
  const highlightedPolylineRef = useRef<L.Polyline | null>(null);

  // Convert GeoJSON coordinates [lon, lat] to Leaflet format [lat, lon]
  const leafletCoordinates = useMemo(() => {
    if (!route || !route.coordinates || route.coordinates.length === 0) {
      return [];
    }
    return route.coordinates.map(([lon, lat]) => [lat, lon] as [number, number]);
  }, [route]);

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

    // Add tile layer
    L.tileLayer('https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png', {
      attribution: '&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a> contributors',
    }).addTo(map);

    // Add polyline
    const polyline = L.polyline(leafletCoordinates, {
      color: '#3b82f6',
      weight: 4,
      opacity: 0.8,
    }).addTo(map);

    // Fit bounds if available
    if (bounds) {
      map.fitBounds(bounds, { padding: [20, 20] });
    }

    // Store references
    mapRef.current = map;
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
      if (polylineRef.current) {
        try {
          mapToCleanup.removeLayer(polylineRef.current);
        } catch (e) {
          // Ignore errors if layer was already removed
        }
        polylineRef.current = null;
      }
      
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
  }, [workoutId, center, bounds, leafletCoordinates, route, interactive]);

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

    // Create highlighted polyline
    const highlightedPolyline = L.polyline(segmentCoordinates, {
      color: '#ef4444',
      weight: 6,
      opacity: 0.9,
    }).addTo(map);

    highlightedPolylineRef.current = highlightedPolyline;
  }, [hoveredSplitIdx, route, splits]);

  if (!route || !route.coordinates || route.coordinates.length === 0) {
    return (
      <div className="flex items-center justify-center h-64 bg-gray-100 dark:bg-gray-800 rounded-lg border border-gray-200 dark:border-gray-700">
        <p className="text-gray-500 dark:text-gray-400">No route data available</p>
      </div>
    );
  }

  return (
    <div
      ref={containerRef}
      className={`w-full ${height} rounded-lg overflow-hidden border border-gray-200 dark:border-gray-700`}
      style={{ position: 'relative', isolation: 'isolate' }}
    />
  );
}

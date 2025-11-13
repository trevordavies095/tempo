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

interface WorkoutMapProps {
  route: RouteGeoJson | null;
  workoutId?: string;
}

export default function WorkoutMap({ route, workoutId }: WorkoutMapProps) {
  // Ref to store the Leaflet map instance
  const mapRef = useRef<L.Map | null>(null);
  // Ref to container div element
  const containerRef = useRef<HTMLDivElement>(null);
  // Ref to store polyline instance for cleanup
  const polylineRef = useRef<L.Polyline | null>(null);

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
      scrollWheelZoom: true,
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
      if (polylineRef.current) {
        map.removeLayer(polylineRef.current);
        polylineRef.current = null;
      }
      if (mapRef.current) {
        mapRef.current.remove();
        mapRef.current = null;
      }
      if (container && (container as any)._leaflet_id) {
        delete (container as any)._leaflet_id;
      }
      if (container && (container as any)._leaflet) {
        delete (container as any)._leaflet;
      }
    };
  }, [workoutId, center, bounds, leafletCoordinates, route]);

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
      className="w-full h-64 rounded-lg overflow-hidden border border-gray-200 dark:border-gray-700"
      style={{ position: 'relative', isolation: 'isolate' }}
    />
  );
}

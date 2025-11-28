'use client';

import { useState, useEffect, useCallback } from 'react';
import { formatDuration, formatDistance } from '@/lib/format';
import type { WorkoutDetail } from '@/lib/api';
import { useSettings } from '@/lib/settings';

interface CropWorkoutDialogProps {
  open: boolean;
  onClose: () => void;
  onConfirm: (startTrimSeconds: number, endTrimSeconds: number) => void;
  workout: WorkoutDetail;
  isLoading?: boolean;
}

const MINIMUM_REMAINING_DURATION_SECONDS = 10;

/**
 * Converts MM:SS format to seconds
 */
function parseTimeString(timeStr: string): number | null {
  const parts = timeStr.split(':');
  if (parts.length === 2) {
    const minutes = parseInt(parts[0], 10);
    const seconds = parseInt(parts[1], 10);
    if (!isNaN(minutes) && !isNaN(seconds) && minutes >= 0 && seconds >= 0 && seconds < 60) {
      return minutes * 60 + seconds;
    }
  }
  return null;
}

/**
 * Converts seconds to MM:SS format
 */
function formatTimeString(seconds: number): string {
  const mins = Math.floor(seconds / 60);
  const secs = seconds % 60;
  return `${mins}:${secs.toString().padStart(2, '0')}`;
}

export function CropWorkoutDialog({
  open,
  onClose,
  onConfirm,
  workout,
  isLoading = false,
}: CropWorkoutDialogProps) {
  const { unitPreference } = useSettings();
  const [startTrimSeconds, setStartTrimSeconds] = useState(0);
  const [endTrimSeconds, setEndTrimSeconds] = useState(0);
  const [startTrimInput, setStartTrimInput] = useState('0:00');
  const [endTrimInput, setEndTrimInput] = useState('0:00');

  const originalDurationS = workout.durationS;
  const originalDistanceM = workout.distanceM;

  // Calculate new values
  const newDurationS = Math.max(0, originalDurationS - startTrimSeconds - endTrimSeconds);
  const newDistanceM = originalDistanceM * (newDurationS / originalDurationS); // Estimate based on time ratio

  // Validation
  const isValid = newDurationS >= MINIMUM_REMAINING_DURATION_SECONDS &&
    startTrimSeconds >= 0 &&
    endTrimSeconds >= 0 &&
    startTrimSeconds + endTrimSeconds < originalDurationS;

  // Update input strings when slider values change
  useEffect(() => {
    setStartTrimInput(formatTimeString(startTrimSeconds));
  }, [startTrimSeconds]);

  useEffect(() => {
    setEndTrimInput(formatTimeString(endTrimSeconds));
  }, [endTrimSeconds]);

  const handleStartTrimInputChange = (value: string) => {
    setStartTrimInput(value);
    const seconds = parseTimeString(value);
    if (seconds !== null && seconds >= 0 && seconds <= originalDurationS - endTrimSeconds - MINIMUM_REMAINING_DURATION_SECONDS) {
      setStartTrimSeconds(seconds);
    }
  };

  const handleEndTrimInputChange = (value: string) => {
    setEndTrimInput(value);
    const seconds = parseTimeString(value);
    if (seconds !== null && seconds >= 0 && seconds <= originalDurationS - startTrimSeconds - MINIMUM_REMAINING_DURATION_SECONDS) {
      setEndTrimSeconds(seconds);
    }
  };

  const handleStartSliderChange = (value: number) => {
    const maxStartTrim = originalDurationS - endTrimSeconds - MINIMUM_REMAINING_DURATION_SECONDS;
    const newStartTrim = Math.max(0, Math.min(value, maxStartTrim));
    setStartTrimSeconds(newStartTrim);
  };

  const handleEndSliderChange = (value: number) => {
    const maxEndTrim = originalDurationS - startTrimSeconds - MINIMUM_REMAINING_DURATION_SECONDS;
    const newEndTrim = Math.max(0, Math.min(value, maxEndTrim));
    setEndTrimSeconds(newEndTrim);
  };

  // Button handlers for start point adjustment
  const handleStartBack = () => {
    if (startTrimSeconds > 0) {
      const newStartTrim = Math.max(0, startTrimSeconds - 1);
      setStartTrimSeconds(newStartTrim);
    }
  };

  const handleStartForward = () => {
    const maxStartTrim = originalDurationS - endTrimSeconds - MINIMUM_REMAINING_DURATION_SECONDS;
    if (startTrimSeconds < maxStartTrim) {
      const newStartTrim = Math.min(maxStartTrim, startTrimSeconds + 1);
      setStartTrimSeconds(newStartTrim);
    }
  };

  // Button handlers for end point adjustment
  const handleEndBack = () => {
    const maxEndTrim = originalDurationS - startTrimSeconds - MINIMUM_REMAINING_DURATION_SECONDS;
    if (endTrimSeconds < maxEndTrim) {
      const newEndTrim = Math.min(maxEndTrim, endTrimSeconds + 1);
      setEndTrimSeconds(newEndTrim);
    }
  };

  const handleEndForward = () => {
    if (endTrimSeconds > 0) {
      const newEndTrim = Math.max(0, endTrimSeconds - 1);
      setEndTrimSeconds(newEndTrim);
    }
  };

  const handleConfirm = () => {
    if (isValid && !isLoading) {
      onConfirm(startTrimSeconds, endTrimSeconds);
    }
  };

  const handleBackdropClick = (e: React.MouseEvent) => {
    if (e.target === e.currentTarget) {
      onClose();
    }
  };

  if (!open) return null;

  const maxStartTrim = originalDurationS - endTrimSeconds - MINIMUM_REMAINING_DURATION_SECONDS;
  const maxEndTrim = originalDurationS - startTrimSeconds - MINIMUM_REMAINING_DURATION_SECONDS;

  return (
    <div
      className="fixed inset-0 z-50 flex items-center justify-center bg-black bg-opacity-50 p-4"
      onClick={handleBackdropClick}
    >
      <div className="bg-white dark:bg-gray-900 rounded-lg shadow-xl max-w-2xl w-full p-6 border border-gray-200 dark:border-gray-800">
        <h2 className="text-xl font-semibold text-gray-900 dark:text-gray-100 mb-4">
          Crop Workout
        </h2>

        <div className="mb-6 space-y-4">
          <p className="text-sm text-gray-600 dark:text-gray-400">
            Trim time from the beginning or end of your workout. The original data will be preserved for audit trail.
          </p>

          {/* Timeline Visualization */}
          <div className="relative">
            <div className="text-xs text-gray-500 dark:text-gray-400 mb-2">
              Timeline (drag handles to adjust)
            </div>
            <div className="relative h-12 bg-gray-200 dark:bg-gray-700 rounded-lg overflow-hidden">
              {/* Trimmed start section */}
              {startTrimSeconds > 0 && (
                <div
                  className="absolute left-0 top-0 h-full bg-red-300 dark:bg-red-900/50"
                  style={{ width: `${(startTrimSeconds / originalDurationS) * 100}%` }}
                />
              )}
              {/* Trimmed end section */}
              {endTrimSeconds > 0 && (
                <div
                  className="absolute right-0 top-0 h-full bg-red-300 dark:bg-red-900/50"
                  style={{ width: `${(endTrimSeconds / originalDurationS) * 100}%` }}
                />
              )}
              {/* Remaining section */}
              <div
                className="absolute left-0 top-0 h-full bg-green-300 dark:bg-green-900/50"
                style={{
                  left: `${(startTrimSeconds / originalDurationS) * 100}%`,
                  width: `${(newDurationS / originalDurationS) * 100}%`,
                }}
              />
              {/* Start handle */}
              <input
                type="range"
                min="0"
                max={maxStartTrim}
                value={startTrimSeconds}
                onChange={(e) => handleStartSliderChange(parseInt(e.target.value, 10))}
                className="absolute left-0 top-0 w-full h-full opacity-0 cursor-pointer z-10"
                style={{ pointerEvents: 'auto' }}
              />
              {/* End handle */}
              <input
                type="range"
                min="0"
                max={maxEndTrim}
                value={endTrimSeconds}
                onChange={(e) => handleEndSliderChange(parseInt(e.target.value, 10))}
                className="absolute right-0 top-0 w-full h-full opacity-0 cursor-pointer z-10"
                style={{ pointerEvents: 'auto' }}
              />
            </div>
            <div className="flex justify-between text-xs text-gray-500 dark:text-gray-400 mt-1">
              <span>Start</span>
              <span>End</span>
            </div>
            {/* Navigation Buttons */}
            <div className="flex justify-between items-center mt-3">
              {/* Left side buttons for start point */}
              <div className="flex gap-2">
                <button
                  onClick={handleStartBack}
                  disabled={startTrimSeconds === 0}
                  className="px-3 py-1.5 text-sm font-medium rounded-md transition-colors bg-gray-100 text-gray-700 dark:bg-gray-800 dark:text-gray-300 hover:bg-gray-200 dark:hover:bg-gray-700 disabled:opacity-50 disabled:cursor-not-allowed disabled:hover:bg-gray-100 dark:disabled:hover:bg-gray-800"
                  title="Move start point later (trim less from start)"
                >
                  ← Back
                </button>
                <button
                  onClick={handleStartForward}
                  disabled={startTrimSeconds >= maxStartTrim}
                  className="px-3 py-1.5 text-sm font-medium rounded-md transition-colors bg-gray-100 text-gray-700 dark:bg-gray-800 dark:text-gray-300 hover:bg-gray-200 dark:hover:bg-gray-700 disabled:opacity-50 disabled:cursor-not-allowed disabled:hover:bg-gray-100 dark:disabled:hover:bg-gray-800"
                  title="Move start point earlier (trim more from start)"
                >
                  Forward →
                </button>
              </div>
              {/* Right side buttons for end point */}
              <div className="flex gap-2">
                <button
                  onClick={handleEndBack}
                  disabled={endTrimSeconds >= maxEndTrim}
                  className="px-3 py-1.5 text-sm font-medium rounded-md transition-colors bg-gray-100 text-gray-700 dark:bg-gray-800 dark:text-gray-300 hover:bg-gray-200 dark:hover:bg-gray-700 disabled:opacity-50 disabled:cursor-not-allowed disabled:hover:bg-gray-100 dark:disabled:hover:bg-gray-800"
                  title="Move end point earlier (trim more from end)"
                >
                  ← Back
                </button>
                <button
                  onClick={handleEndForward}
                  disabled={endTrimSeconds === 0}
                  className="px-3 py-1.5 text-sm font-medium rounded-md transition-colors bg-gray-100 text-gray-700 dark:bg-gray-800 dark:text-gray-300 hover:bg-gray-200 dark:hover:bg-gray-700 disabled:opacity-50 disabled:cursor-not-allowed disabled:hover:bg-gray-100 dark:disabled:hover:bg-gray-800"
                  title="Move end point later (trim less from end)"
                >
                  Forward →
                </button>
              </div>
            </div>
          </div>

          {/* Trim Inputs */}
          <div className="grid grid-cols-2 gap-4">
            <div>
              <label className="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-1">
                Trim from Start
              </label>
              <div className="flex gap-2">
                <input
                  type="text"
                  value={startTrimInput}
                  onChange={(e) => handleStartTrimInputChange(e.target.value)}
                  placeholder="M:SS"
                  className="flex-1 px-3 py-2 border border-gray-300 dark:border-gray-600 rounded-md bg-white dark:bg-gray-800 text-gray-900 dark:text-gray-100 focus:outline-none focus:ring-2 focus:ring-blue-500"
                />
                <div className="px-3 py-2 text-sm text-gray-600 dark:text-gray-400">
                  {formatDuration(startTrimSeconds)}
                </div>
              </div>
            </div>
            <div>
              <label className="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-1">
                Trim from End
              </label>
              <div className="flex gap-2">
                <input
                  type="text"
                  value={endTrimInput}
                  onChange={(e) => handleEndTrimInputChange(e.target.value)}
                  placeholder="M:SS"
                  className="flex-1 px-3 py-2 border border-gray-300 dark:border-gray-600 rounded-md bg-white dark:bg-gray-800 text-gray-900 dark:text-gray-100 focus:outline-none focus:ring-2 focus:ring-blue-500"
                />
                <div className="px-3 py-2 text-sm text-gray-600 dark:text-gray-400">
                  {formatDuration(endTrimSeconds)}
                </div>
              </div>
            </div>
          </div>

          {/* Preview */}
          <div className="bg-gray-50 dark:bg-gray-800 rounded-lg p-4 border border-gray-200 dark:border-gray-700">
            <div className="text-sm font-medium text-gray-700 dark:text-gray-300 mb-2">Preview</div>
            <div className="grid grid-cols-2 gap-4 text-sm">
              <div>
                <div className="text-gray-500 dark:text-gray-400">Original</div>
                <div className="font-semibold text-gray-900 dark:text-gray-100">
                  {formatDuration(originalDurationS)} • {formatDistance(originalDistanceM, unitPreference)}
                </div>
              </div>
              <div>
                <div className="text-gray-500 dark:text-gray-400">After Crop</div>
                <div className="font-semibold text-gray-900 dark:text-gray-100">
                  {formatDuration(newDurationS)} • {formatDistance(newDistanceM, unitPreference)}
                </div>
              </div>
            </div>
            {!isValid && newDurationS < MINIMUM_REMAINING_DURATION_SECONDS && (
              <div className="mt-2 text-xs text-red-600 dark:text-red-400">
                Minimum remaining duration is {formatDuration(MINIMUM_REMAINING_DURATION_SECONDS)}
              </div>
            )}
          </div>

          <p className="text-xs text-red-600 dark:text-red-400 font-medium">
            This action cannot be undone. Original data will be preserved for audit trail.
          </p>
        </div>

        <div className="flex gap-3 justify-end">
          <button
            onClick={onClose}
            disabled={isLoading}
            className="px-4 py-2 rounded-lg font-medium transition-colors bg-gray-100 text-gray-700 dark:bg-gray-800 dark:text-gray-300 hover:bg-gray-200 dark:hover:bg-gray-700 disabled:opacity-50 disabled:cursor-not-allowed"
          >
            Cancel
          </button>
          <button
            onClick={handleConfirm}
            disabled={!isValid || isLoading}
            className="px-4 py-2 rounded-lg font-medium transition-colors bg-blue-600 text-white hover:bg-blue-700 dark:bg-blue-500 dark:hover:bg-blue-600 disabled:opacity-50 disabled:cursor-not-allowed"
          >
            {isLoading ? 'Cropping...' : 'Crop Workout'}
          </button>
        </div>
      </div>
    </div>
  );
}


'use client';

import { useMutation } from '@tanstack/react-query';
import { useCallback, useState } from 'react';
import { importWorkoutFile } from '@/lib/api';

export function FileUpload() {
  const [dragActive, setDragActive] = useState(false);
  const [selectedFile, setSelectedFile] = useState<File | null>(null);

  const mutation = useMutation({
    mutationFn: importWorkoutFile,
    onSuccess: (data) => {
      alert(`Workout imported successfully!\nDistance: ${(data.distanceM / 1000).toFixed(2)} km\nDuration: ${Math.floor(data.durationS / 60)}:${(data.durationS % 60).toString().padStart(2, '0')}`);
      setSelectedFile(null);
    },
    onError: (error: Error) => {
      alert(`Error importing file: ${error.message}`);
    },
  });

  const handleDrag = useCallback((e: React.DragEvent) => {
    e.preventDefault();
    e.stopPropagation();
    if (e.type === 'dragenter' || e.type === 'dragover') {
      setDragActive(true);
    } else if (e.type === 'dragleave') {
      setDragActive(false);
    }
  }, []);

  const handleDrop = useCallback(
    (e: React.DragEvent) => {
      e.preventDefault();
      e.stopPropagation();
      setDragActive(false);

      if (e.dataTransfer.files && e.dataTransfer.files[0]) {
        const file = e.dataTransfer.files[0];
        const fileName = file.name.toLowerCase();
        if (fileName.endsWith('.gpx') || fileName.endsWith('.fit') || fileName.endsWith('.fit.gz')) {
          setSelectedFile(file);
        } else {
          alert('Please upload a GPX or FIT file');
        }
      }
    },
    []
  );

  const handleFileInput = useCallback((e: React.ChangeEvent<HTMLInputElement>) => {
    if (e.target.files && e.target.files[0]) {
      const file = e.target.files[0];
      const fileName = file.name.toLowerCase();
      if (fileName.endsWith('.gpx') || fileName.endsWith('.fit') || fileName.endsWith('.fit.gz')) {
        setSelectedFile(file);
      } else {
        alert('Please upload a GPX or FIT file');
      }
    }
  }, []);

  const handleSubmit = useCallback(
    (e: React.FormEvent) => {
      e.preventDefault();
      if (selectedFile) {
        mutation.mutate(selectedFile);
      }
    },
    [selectedFile, mutation]
  );

  return (
    <div className="w-full max-w-2xl">
      <form onSubmit={handleSubmit} className="space-y-4">
        <div
          onDragEnter={handleDrag}
          onDragLeave={handleDrag}
          onDragOver={handleDrag}
          onDrop={handleDrop}
          className={`relative border-2 border-dashed rounded-lg p-8 transition-colors ${
            dragActive
              ? 'border-blue-500 bg-blue-50 dark:bg-blue-950'
              : 'border-gray-300 dark:border-gray-700 bg-gray-50 dark:bg-gray-900'
          }`}
        >
          <input
            type="file"
            id="file-upload"
            accept=".gpx,.fit,.fit.gz"
            onChange={handleFileInput}
            className="absolute inset-0 w-full h-full opacity-0 cursor-pointer"
          />
          <div className="text-center">
            <svg
              className="mx-auto h-12 w-12 text-gray-400"
              stroke="currentColor"
              fill="none"
              viewBox="0 0 48 48"
            >
              <path
                d="M28 8H12a4 4 0 00-4 4v20m32-12v8m0 0v8a4 4 0 01-4 4H12a4 4 0 01-4-4v-4m32-4l-3.172-3.172a4 4 0 00-5.656 0L28 28M8 32l9.172-9.172a4 4 0 015.656 0L28 28m0 0l4 4m4-4h12m-4-4v12m0 0l-4-4m4 4l-4-4"
                strokeWidth={2}
                strokeLinecap="round"
                strokeLinejoin="round"
              />
            </svg>
            <p className="mt-2 text-sm text-gray-600 dark:text-gray-400">
              <span className="font-semibold">Click to upload</span> or drag and drop
            </p>
            <p className="text-xs text-gray-500 dark:text-gray-500">GPX or FIT files</p>
            {selectedFile && (
              <p className="mt-2 text-sm font-medium text-gray-900 dark:text-gray-100">
                Selected: {selectedFile.name}
              </p>
            )}
          </div>
        </div>

        {selectedFile && (
          <button
            type="submit"
            disabled={mutation.isPending}
            className="w-full px-4 py-2 bg-blue-600 text-white rounded-lg hover:bg-blue-700 disabled:opacity-50 disabled:cursor-not-allowed transition-colors"
          >
            {mutation.isPending ? 'Uploading...' : 'Import Workout'}
          </button>
        )}

        {mutation.isError && (
          <div className="p-4 bg-red-50 dark:bg-red-900/20 border border-red-200 dark:border-red-800 rounded-lg">
            <p className="text-sm text-red-800 dark:text-red-200">
              Error: {mutation.error instanceof Error ? mutation.error.message : 'Unknown error'}
            </p>
          </div>
        )}
      </form>
    </div>
  );
}


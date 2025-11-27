import { useCallback, useState } from 'react';

interface UseFileDropOptions {
  onFilesSelected: (files: File[]) => void;
  acceptExtensions?: string[];
  maxFiles?: number;
}

/**
 * Hook for handling file drag and drop functionality
 * @param options - Configuration options
 * @returns Object with dragActive state and event handlers
 */
export function useFileDrop({
  onFilesSelected,
  acceptExtensions = [],
  maxFiles,
}: UseFileDropOptions) {
  const [dragActive, setDragActive] = useState(false);

  const isValidFile = useCallback(
    (fileName: string): boolean => {
      if (acceptExtensions.length === 0) {
        return true;
      }
      const lower = fileName.toLowerCase();
      return acceptExtensions.some((ext) => lower.endsWith(ext.toLowerCase()));
    },
    [acceptExtensions]
  );

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

      if (e.dataTransfer.files && e.dataTransfer.files.length > 0) {
        const files = Array.from(e.dataTransfer.files);
        const validFiles = files.filter((file) => isValidFile(file.name));

        if (validFiles.length !== files.length) {
          const extensions = acceptExtensions.join(', ');
          alert(`Some files were skipped. Only ${extensions} files are supported.`);
        }

        if (validFiles.length > 0) {
          const filesToUse = maxFiles ? validFiles.slice(0, maxFiles) : validFiles;
          onFilesSelected(filesToUse);
        }
      }
    },
    [isValidFile, onFilesSelected, acceptExtensions, maxFiles]
  );

  const handleFileInput = useCallback(
    (e: React.ChangeEvent<HTMLInputElement>) => {
      if (e.target.files && e.target.files.length > 0) {
        const files = Array.from(e.target.files);
        const validFiles = files.filter((file) => isValidFile(file.name));

        if (validFiles.length !== files.length) {
          const extensions = acceptExtensions.join(', ');
          alert(`Some files were skipped. Only ${extensions} files are supported.`);
        }

        if (validFiles.length > 0) {
          const filesToUse = maxFiles ? validFiles.slice(0, maxFiles) : validFiles;
          onFilesSelected(filesToUse);
        }
      }
      // Reset input so same files can be selected again
      e.target.value = '';
    },
    [isValidFile, onFilesSelected, acceptExtensions, maxFiles]
  );

  return {
    dragActive,
    handleDrag,
    handleDrop,
    handleFileInput,
  };
}


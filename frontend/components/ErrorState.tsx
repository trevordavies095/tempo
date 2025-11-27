interface ErrorStateProps {
  error: Error | unknown;
  message?: string;
  className?: string;
}

export default function ErrorState({ error, message, className = '' }: ErrorStateProps) {
  const errorMessage = error instanceof Error ? error.message : message || 'An error occurred';
  
  return (
    <div className={`flex items-center justify-center ${className}`}>
      <p className="text-sm text-red-600 dark:text-red-400">
        Error: {errorMessage}
      </p>
    </div>
  );
}


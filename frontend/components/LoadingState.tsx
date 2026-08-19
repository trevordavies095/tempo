interface LoadingStateProps {
  message?: string;
  className?: string;
}

export default function LoadingState({ message = 'Loading...', className = '' }: LoadingStateProps) {
  return (
    <div className={`flex items-center justify-center ${className}`}>
      <p className="text-sm text-muted">{message}</p>
    </div>
  );
}

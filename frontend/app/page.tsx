'use client';

import { useRouter } from 'next/navigation';
import { useEffect } from 'react';
import { useAuth } from '@/contexts/AuthContext';

export default function Home() {
  const router = useRouter();
  const { isAuthenticated, isLoading } = useAuth();

  useEffect(() => {
    if (!isLoading) {
      if (isAuthenticated) {
        router.push('/dashboard');
      } else {
        router.push('/login');
      }
    }
  }, [isAuthenticated, isLoading, router]);

  // Show loading state while checking
  return (
    <div className="flex min-h-screen items-center justify-center bg-canvas">
      <main className="flex min-h-screen w-full max-w-4xl flex-col items-center justify-start py-16 px-8">
        <div className="w-full mb-8">
          <div>
            <h1 className="text-4xl font-bold text-ink mb-2">
              Tempo
            </h1>
            <p className="text-lg text-muted">
              Self-hostable running tracker
            </p>
          </div>
        </div>
        <div className="w-full text-center">
          <p className="text-muted">Loading...</p>
        </div>
      </main>
    </div>
  );
}

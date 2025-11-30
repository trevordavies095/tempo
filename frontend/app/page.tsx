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
    <div className="flex min-h-screen items-center justify-center bg-zinc-50 dark:bg-black">
      <main className="flex min-h-screen w-full max-w-4xl flex-col items-center justify-start py-16 px-8">
        <div className="w-full mb-8">
          <div>
            <h1 className="text-4xl font-bold text-gray-900 dark:text-gray-100 mb-2">
              Tempo
            </h1>
            <p className="text-lg text-gray-600 dark:text-gray-400">
              Self-hostable running tracker
            </p>
          </div>
        </div>
        <div className="w-full text-center">
          <p className="text-gray-600 dark:text-gray-400">Loading...</p>
        </div>
      </main>
    </div>
  );
}

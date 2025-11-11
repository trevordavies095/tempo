'use client';

import { useQuery } from '@tanstack/react-query';
import { useRouter } from 'next/navigation';
import { useEffect } from 'react';
import { getWorkouts } from '@/lib/api';

export default function Home() {
  const router = useRouter();

  const { data, isLoading, isError } = useQuery({
    queryKey: ['workouts', 'count'],
    queryFn: () => getWorkouts({ page: 1, pageSize: 1 }),
  });

  useEffect(() => {
    if (data) {
      if (data.totalCount > 0) {
        router.push('/dashboard');
      } else {
        router.push('/import');
      }
    } else if (isError) {
      // On error, redirect to import page as fallback
      router.push('/import');
    }
  }, [data, isError, router]);

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

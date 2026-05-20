'use client';

import Link from 'next/link';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { getShoes, updateShoe } from '@/lib/api';
import { AuthGuard } from '@/components/AuthGuard';

function RetiredShoesContent() {
  const queryClient = useQueryClient();
  const { data: shoes, isLoading } = useQuery({
    queryKey: ['shoes', 'retired'],
    queryFn: () => getShoes({ status: 'retired' }),
  });

  const unretireMutation = useMutation({
    mutationFn: (id: string) => updateShoe(id, { isRetired: false }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['shoes'] });
      queryClient.invalidateQueries({ queryKey: ['default-shoe'] });
    },
  });

  return (
    <div className="flex min-h-screen items-start justify-center bg-zinc-50 dark:bg-black">
      <main className="flex min-h-screen w-full max-w-4xl flex-col items-start py-16 px-8">
        <div className="w-full mb-8">
          <p className="mb-2">
            <Link
              href="/settings"
              className="text-sm text-blue-600 hover:text-blue-800 dark:text-blue-400 dark:hover:text-blue-300"
            >
              ← Back to Settings
            </Link>
          </p>
          <h1 className="text-4xl font-bold text-gray-900 dark:text-gray-100 mb-2">Retired shoes</h1>
          <p className="text-lg text-gray-600 dark:text-gray-400">
            Shoes you retired stay here with their mileage. Un-retire to use them in lists again.
          </p>
        </div>

        <div className="w-full bg-white dark:bg-gray-900 p-6 rounded-lg border border-gray-200 dark:border-gray-800">
          {isLoading ? (
            <p className="text-gray-600 dark:text-gray-400">Loading…</p>
          ) : !shoes?.length ? (
            <p className="text-gray-600 dark:text-gray-400">No retired shoes.</p>
          ) : (
            <ul className="space-y-4">
              {shoes.map((shoe) => (
                <li
                  key={shoe.id}
                  className="flex flex-col sm:flex-row sm:items-center sm:justify-between gap-3 p-4 bg-gray-50 dark:bg-gray-800 rounded-lg border border-gray-200 dark:border-gray-700"
                >
                  <div>
                    <h2 className="text-lg font-semibold text-gray-900 dark:text-gray-100">
                      {shoe.brand} {shoe.model}
                    </h2>
                    <p className="text-sm text-gray-600 dark:text-gray-400">
                      Total: {shoe.totalMileage.toFixed(1)} {shoe.unit}
                    </p>
                  </div>
                  <button
                    type="button"
                    onClick={() => unretireMutation.mutate(shoe.id)}
                    disabled={unretireMutation.isPending}
                    className="px-4 py-2 text-sm bg-blue-600 text-white rounded-lg hover:bg-blue-700 dark:bg-blue-500 dark:hover:bg-blue-600 disabled:opacity-50 shrink-0"
                  >
                    {unretireMutation.isPending ? 'Saving…' : 'Un-retire'}
                  </button>
                </li>
              ))}
            </ul>
          )}
        </div>
      </main>
    </div>
  );
}

export default function RetiredShoesPage() {
  return (
    <AuthGuard>
      <RetiredShoesContent />
    </AuthGuard>
  );
}

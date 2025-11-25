'use client';

import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { useState } from 'react';
import { SettingsProvider } from '@/lib/settings';

export function Providers({ children }: { children: React.ReactNode }) {
  const [queryClient] = useState(
    () =>
      new QueryClient({
        defaultOptions: {
          queries: {
            staleTime: 60 * 1000, // 1 minute
          },
        },
      })
  );

  return (
    <QueryClientProvider client={queryClient}>
      <SettingsProvider>{children}</SettingsProvider>
    </QueryClientProvider>
  );
}


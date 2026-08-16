import type { ReactNode } from 'react';

type Density = 'control' | 'overview';

const densityInner: Record<Density, string> = {
  control: 'mx-auto w-full max-w-4xl py-8 px-6',
  overview: 'mx-auto w-full max-w-6xl py-10 px-6',
};

export function PageShell({
  title,
  subtitle,
  density = 'control',
  leading,
  children,
}: {
  title: string;
  subtitle?: string;
  density?: Density;
  leading?: ReactNode;
  children: ReactNode;
}) {
  return (
    <div className="min-h-screen bg-canvas text-ink">
      <main className={densityInner[density]}>
        {leading ? <div className="mb-3">{leading}</div> : null}
        <header className="mb-6">
          <h1 className="text-2xl font-bold text-ink">{title}</h1>
          {subtitle ? (
            <p className="mt-1 text-sm text-muted">{subtitle}</p>
          ) : null}
        </header>
        {children}
      </main>
    </div>
  );
}

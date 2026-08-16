'use client';

import { useRef, type KeyboardEvent, type ReactNode } from 'react';

export type TabItem<T extends string = string> = {
  id: T;
  label: string;
};

export function Tabs<T extends string>({
  items,
  value,
  onChange,
  'aria-label': ariaLabel = 'Tabs',
  children,
}: {
  items: TabItem<T>[];
  value: T;
  onChange: (id: T) => void;
  'aria-label'?: string;
  children?: ReactNode;
}) {
  const buttonRefs = useRef<Partial<Record<T, HTMLButtonElement | null>>>({});

  const handleKeyDown = (event: KeyboardEvent<HTMLButtonElement>, id: T) => {
    if (event.key !== 'ArrowLeft' && event.key !== 'ArrowRight') {
      return;
    }
    event.preventDefault();
    const currentIndex = items.findIndex((item) => item.id === id);
    if (currentIndex < 0 || items.length === 0) {
      return;
    }
    const delta = event.key === 'ArrowLeft' ? -1 : 1;
    const nextItem = items[(currentIndex + delta + items.length) % items.length];
    onChange(nextItem.id);
    queueMicrotask(() => {
      buttonRefs.current[nextItem.id]?.focus();
    });
  };

  return (
    <div className="w-full">
      <nav className="mb-4 border-b border-border" aria-label={ariaLabel}>
        <div className="flex gap-1" role="tablist">
          {items.map((item) => {
            const selected = item.id === value;
            return (
              <button
                key={item.id}
                ref={(el) => {
                  buttonRefs.current[item.id] = el;
                }}
                type="button"
                role="tab"
                id={`tab-${item.id}`}
                aria-selected={selected}
                tabIndex={selected ? 0 : -1}
                onClick={() => onChange(item.id)}
                onKeyDown={(event) => handleKeyDown(event, item.id)}
                className={`px-4 py-2 text-sm font-medium rounded-t-tempo border-b-2 transition-colors focus:outline-none focus:ring-2 focus:ring-volt focus:ring-offset-2 focus:ring-offset-canvas ${
                  selected
                    ? 'border-ink text-ink bg-canvas dark:border-volt dark:text-volt dark:bg-transparent'
                    : 'border-transparent text-muted hover:text-ink hover:bg-canvas'
                }`}
              >
                {item.label}
              </button>
            );
          })}
        </div>
      </nav>
      <div role="tabpanel" aria-labelledby={`tab-${value}`}>
        {children}
      </div>
    </div>
  );
}

import type { ButtonHTMLAttributes } from 'react';

type Variant = 'primary' | 'secondary' | 'danger' | 'ghost';
type Size = 'sm' | 'md';

const variants: Record<Variant, string> = {
  primary:
    'bg-ink text-inverse hover:opacity-90 dark:bg-volt dark:text-on-volt',
  secondary:
    'bg-canvas text-ink border border-border hover:bg-raised',
  danger: 'bg-danger text-on-danger hover:opacity-90',
  ghost: 'bg-transparent text-muted hover:text-ink hover:bg-canvas',
};

const sizes: Record<Size, string> = {
  sm: 'px-3 py-1.5 text-sm font-medium',
  md: 'px-6 py-3 text-sm font-medium',
};

export function Button({
  variant = 'primary',
  size = 'md',
  className = '',
  disabled,
  type = 'button',
  ...props
}: ButtonHTMLAttributes<HTMLButtonElement> & {
  variant?: Variant;
  size?: Size;
}) {
  return (
    <button
      type={type}
      disabled={disabled}
      className={`inline-flex items-center justify-center rounded-tempo transition-opacity focus:outline-none focus:ring-2 focus:ring-volt focus:ring-offset-2 focus:ring-offset-raised disabled:opacity-50 disabled:cursor-not-allowed ${variants[variant]} ${sizes[size]} ${className}`}
      {...props}
    />
  );
}

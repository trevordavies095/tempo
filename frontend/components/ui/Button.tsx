import type { ButtonHTMLAttributes, ReactNode } from 'react';
import Link from 'next/link';

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

function buttonClassName(variant: Variant, size: Size, className: string): string {
  return `inline-flex items-center justify-center rounded-tempo transition-opacity focus:outline-none focus:ring-2 focus:ring-volt focus:ring-offset-2 focus:ring-offset-raised disabled:opacity-50 disabled:cursor-not-allowed ${variants[variant]} ${sizes[size]} ${className}`;
}

type ButtonBaseProps = {
  variant?: Variant;
  size?: Size;
  className?: string;
  children?: ReactNode;
};

type ButtonAsButton = ButtonBaseProps &
  Omit<ButtonHTMLAttributes<HTMLButtonElement>, 'className' | 'children'> & {
    href?: undefined;
  };

type ButtonAsLink = ButtonBaseProps & {
  href: string;
  prefetch?: boolean;
  replace?: boolean;
  scroll?: boolean;
  target?: string;
  rel?: string;
  onClick?: ButtonHTMLAttributes<HTMLAnchorElement>['onClick'];
};

export type ButtonProps = ButtonAsButton | ButtonAsLink;

export function Button(props: ButtonProps) {
  if (typeof props.href === 'string') {
    const {
      href,
      prefetch,
      replace,
      scroll,
      target,
      rel,
      onClick,
      children,
      variant = 'primary',
      size = 'md',
      className = '',
    } = props;
    return (
      <Link
        href={href}
        prefetch={prefetch}
        replace={replace}
        scroll={scroll}
        target={target}
        rel={rel}
        onClick={onClick}
        className={buttonClassName(variant, size, className)}
      >
        {children}
      </Link>
    );
  }

  const {
    variant = 'primary',
    size = 'md',
    className = '',
    children,
    type = 'button',
    disabled,
    ...buttonRest
  } = props;

  return (
    <button
      type={type}
      disabled={disabled}
      className={buttonClassName(variant, size, className)}
      {...buttonRest}
    >
      {children}
    </button>
  );
}

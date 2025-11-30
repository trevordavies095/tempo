'use client';

import { useQuery } from '@tanstack/react-query';
import { getShoes, type Shoe } from '@/lib/api';

interface ShoeSelectorProps {
  value: string | null;
  onChange: (shoeId: string | null) => void;
  showMileage?: boolean;
  className?: string;
}

export function ShoeSelector({ value, onChange, showMileage = false, className = '' }: ShoeSelectorProps) {
  const { data: shoes, isLoading } = useQuery({
    queryKey: ['shoes'],
    queryFn: getShoes,
  });

  if (isLoading) {
    return (
      <select className={className} disabled>
        <option>Loading shoes...</option>
      </select>
    );
  }

  const formatShoeLabel = (shoe: Shoe) => {
    if (showMileage) {
      return `${shoe.brand} ${shoe.model} (${shoe.totalMileage.toFixed(1)} ${shoe.unit})`;
    }
    return `${shoe.brand} ${shoe.model}`;
  };

  return (
    <select
      value={value || ''}
      onChange={(e) => onChange(e.target.value === '' ? null : e.target.value)}
      className={className}
    >
      <option value="">None</option>
      {shoes?.map((shoe) => (
        <option key={shoe.id} value={shoe.id}>
          {formatShoeLabel(shoe)}
        </option>
      ))}
    </select>
  );
}


'use client';

import { useQuery } from '@tanstack/react-query';
import { getShoes, type Shoe } from '@/lib/api';

interface ShoeSelectorProps {
  value: string | null;
  onChange: (shoeId: string | null) => void;
  showMileage?: boolean;
  className?: string;
  /** When the workout is assigned a retired shoe, pass it so the select can show the current value. */
  assignedShoe?: { id: string; brand: string; model: string } | null;
}

export function ShoeSelector({ value, onChange, showMileage = false, className = '', assignedShoe = null }: ShoeSelectorProps) {
  const { data: shoes, isLoading } = useQuery({
    queryKey: ['shoes', 'active'],
    queryFn: () => getShoes({ status: 'active' }),
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

  const showRetiredAssigned =
    !!value &&
    !!assignedShoe &&
    assignedShoe.id === value &&
    !shoes?.some((s) => s.id === value);

  return (
    <select
      value={value || ''}
      onChange={(e) => onChange(e.target.value === '' ? null : e.target.value)}
      className={className}
    >
      <option value="">None</option>
      {showRetiredAssigned && assignedShoe && (
        <option key={assignedShoe.id} value={assignedShoe.id}>
          {assignedShoe.brand} {assignedShoe.model} (retired)
        </option>
      )}
      {shoes?.map((shoe) => (
        <option key={shoe.id} value={shoe.id}>
          {formatShoeLabel(shoe)}
        </option>
      ))}
    </select>
  );
}


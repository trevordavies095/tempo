'use client';

import { useState } from 'react';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import {
  getShoes,
  createShoe,
  updateShoe,
  deleteShoe,
  getDefaultShoe,
  setDefaultShoe,
  type Shoe,
  type CreateShoeRequest,
  type UpdateShoeRequest,
} from '@/lib/api';
import { useSettings } from '@/lib/settings';

export function ShoeManagementSection() {
  const queryClient = useQueryClient();
  const { unitPreference } = useSettings();
  const [isCreating, setIsCreating] = useState(false);
  const [editingId, setEditingId] = useState<string | null>(null);
  const [newShoe, setNewShoe] = useState<CreateShoeRequest>({ brand: '', model: '', initialMileageM: null });
  const [editShoe, setEditShoe] = useState<UpdateShoeRequest>({ brand: '', model: '', initialMileageM: null });
  // Store user input in their preferred units (for display/editing)
  const [newShoeInitialMileageInput, setNewShoeInitialMileageInput] = useState<string>('');
  const [editShoeInitialMileageInput, setEditShoeInitialMileageInput] = useState<string>('');

  // Convert from user's preferred units to meters
  const convertToMeters = (value: number, unit: 'metric' | 'imperial'): number => {
    if (unit === 'imperial') {
      // Convert miles to meters (1 mile = 1609.344 meters)
      return value * 1609.344;
    } else {
      // Convert km to meters (1 km = 1000 meters)
      return value * 1000;
    }
  };

  // Convert from meters to user's preferred units
  const convertFromMeters = (meters: number | null, unit: 'metric' | 'imperial'): number | null => {
    if (meters === null) return null;
    if (unit === 'imperial') {
      // Convert meters to miles
      return meters / 1609.344;
    } else {
      // Convert meters to km
      return meters / 1000;
    }
  };

  const { data: shoes, isLoading } = useQuery({
    queryKey: ['shoes'],
    queryFn: getShoes,
  });

  const { data: defaultShoe } = useQuery({
    queryKey: ['default-shoe'],
    queryFn: getDefaultShoe,
  });

  const createMutation = useMutation({
    mutationFn: createShoe,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['shoes'] });
      setIsCreating(false);
      setNewShoe({ brand: '', model: '', initialMileageM: null });
    },
  });

  const updateMutation = useMutation({
    mutationFn: ({ id, data }: { id: string; data: UpdateShoeRequest }) => updateShoe(id, data),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['shoes'] });
      queryClient.invalidateQueries({ queryKey: ['default-shoe'] });
      setEditingId(null);
      setEditShoe({ brand: '', model: '', initialMileageM: null });
    },
  });

  const deleteMutation = useMutation({
    mutationFn: deleteShoe,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['shoes'] });
      queryClient.invalidateQueries({ queryKey: ['default-shoe'] });
      queryClient.invalidateQueries({ queryKey: ['workout'] });
    },
  });

  const setDefaultMutation = useMutation({
    mutationFn: setDefaultShoe,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['default-shoe'] });
    },
  });

  const handleCreate = () => {
    if (!newShoe.brand.trim() || !newShoe.model.trim()) {
      return;
    }
    // Convert initial mileage from user's preferred units to meters
    const initialMileageM = newShoeInitialMileageInput
      ? convertToMeters(parseFloat(newShoeInitialMileageInput), unitPreference)
      : null;
    createMutation.mutate({ ...newShoe, initialMileageM });
  };

  const handleUpdate = (id: string) => {
    // Convert initial mileage from user's preferred units to meters
    const initialMileageM = editShoeInitialMileageInput
      ? convertToMeters(parseFloat(editShoeInitialMileageInput), unitPreference)
      : null;
    updateMutation.mutate({ id, data: { ...editShoe, initialMileageM } });
  };

  const handleDelete = (id: string) => {
    if (window.confirm('Are you sure you want to delete this shoe? All workouts assigned to this shoe will have their shoe assignment removed.')) {
      deleteMutation.mutate(id);
    }
  };

  const handleSetDefault = (id: string | null) => {
    setDefaultMutation.mutate(id);
  };

  const startEdit = (shoe: Shoe) => {
    setEditingId(shoe.id);
    setEditShoe({
      brand: shoe.brand,
      model: shoe.model,
      initialMileageM: null, // We'll use the input field instead
    });
    // Convert initial mileage from meters to user's preferred units for display
    const initialMileageInUserUnits = convertFromMeters(shoe.initialMileageM, unitPreference);
    setEditShoeInitialMileageInput(initialMileageInUserUnits?.toFixed(1) || '');
  };

  const cancelEdit = () => {
    setEditingId(null);
    setEditShoe({ brand: '', model: '', initialMileageM: null });
    setEditShoeInitialMileageInput('');
  };

  if (isLoading) {
    return (
      <div className="bg-white dark:bg-gray-900 p-6 rounded-lg border border-gray-200 dark:border-gray-800">
        <p className="text-gray-600 dark:text-gray-400">Loading shoes...</p>
      </div>
    );
  }

  return (
    <div className="bg-white dark:bg-gray-900 p-6 rounded-lg border border-gray-200 dark:border-gray-800">
      <h2 className="text-xl font-semibold text-gray-900 dark:text-gray-100 mb-4">
        Shoe Management
      </h2>
      <p className="text-sm text-gray-600 dark:text-gray-400 mb-6">
        Track mileage on your running shoes. Assign shoes to workouts to automatically calculate total mileage.
      </p>

      {/* Default Shoe Selector */}
      <div className="mb-6">
        <label className="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-2">
          Default Shoe
        </label>
        <select
          value={defaultShoe?.defaultShoeId || ''}
          onChange={(e) => handleSetDefault(e.target.value === '' ? null : e.target.value)}
          className="w-full px-3 py-2 border border-gray-300 dark:border-gray-600 rounded-lg bg-white dark:bg-gray-800 text-gray-900 dark:text-gray-100"
        >
          <option value="">None</option>
          {shoes?.map((shoe) => (
            <option key={shoe.id} value={shoe.id}>
              {shoe.brand} {shoe.model}
            </option>
          ))}
        </select>
        <p className="text-xs text-gray-500 dark:text-gray-400 mt-1">
          New workouts will automatically be assigned to the default shoe.
        </p>
      </div>

      {/* Create New Shoe */}
      {!isCreating ? (
        <button
          onClick={() => setIsCreating(true)}
          className="mb-6 px-4 py-2 bg-blue-600 text-white rounded-lg hover:bg-blue-700 dark:bg-blue-500 dark:hover:bg-blue-600"
        >
          + Add New Shoe
        </button>
      ) : (
        <div className="mb-6 p-4 bg-gray-50 dark:bg-gray-800 rounded-lg border border-gray-200 dark:border-gray-700">
          <h3 className="text-sm font-semibold text-gray-900 dark:text-gray-100 mb-3">Add New Shoe</h3>
          <div className="space-y-3">
            <div>
              <label className="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-1">
                Brand *
              </label>
              <input
                type="text"
                value={newShoe.brand}
                onChange={(e) => setNewShoe({ ...newShoe, brand: e.target.value })}
                className="w-full px-3 py-2 border border-gray-300 dark:border-gray-600 rounded-lg bg-white dark:bg-gray-800 text-gray-900 dark:text-gray-100"
                placeholder="e.g., Nike"
              />
            </div>
            <div>
              <label className="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-1">
                Model *
              </label>
              <input
                type="text"
                value={newShoe.model}
                onChange={(e) => setNewShoe({ ...newShoe, model: e.target.value })}
                className="w-full px-3 py-2 border border-gray-300 dark:border-gray-600 rounded-lg bg-white dark:bg-gray-800 text-gray-900 dark:text-gray-100"
                placeholder="e.g., Air Zoom Pegasus 40"
              />
            </div>
            <div>
              <label className="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-1">
                Initial Mileage (optional)
              </label>
              <input
                type="number"
                step="0.1"
                value={newShoeInitialMileageInput}
                onChange={(e) => setNewShoeInitialMileageInput(e.target.value)}
                className="w-full px-3 py-2 border border-gray-300 dark:border-gray-600 rounded-lg bg-white dark:bg-gray-800 text-gray-900 dark:text-gray-100"
                placeholder={unitPreference === 'imperial' ? 'Miles already on shoe' : 'Kilometers already on shoe'}
              />
              <p className="text-xs text-gray-500 dark:text-gray-400 mt-1">
                Enter mileage in {unitPreference === 'imperial' ? 'miles' : 'kilometers'}
              </p>
            </div>
            <div className="flex gap-2">
              <button
                onClick={handleCreate}
                disabled={!newShoe.brand.trim() || !newShoe.model.trim() || createMutation.isPending}
                className="px-4 py-2 bg-blue-600 text-white rounded-lg hover:bg-blue-700 dark:bg-blue-500 dark:hover:bg-blue-600 disabled:bg-gray-400 disabled:cursor-not-allowed"
              >
                {createMutation.isPending ? 'Creating...' : 'Create'}
              </button>
              <button
                onClick={() => {
                  setIsCreating(false);
                  setNewShoe({ brand: '', model: '', initialMileageM: null });
                  setNewShoeInitialMileageInput('');
                }}
                className="px-4 py-2 bg-gray-200 text-gray-700 rounded-lg hover:bg-gray-300 dark:bg-gray-700 dark:text-gray-300 dark:hover:bg-gray-600"
              >
                Cancel
              </button>
            </div>
          </div>
        </div>
      )}

      {/* Shoe List */}
      <div className="space-y-4">
        {shoes && shoes.length === 0 ? (
          <p className="text-gray-600 dark:text-gray-400">No shoes added yet. Add your first shoe above.</p>
        ) : (
          shoes?.map((shoe) => (
            <div
              key={shoe.id}
              className="p-4 bg-gray-50 dark:bg-gray-800 rounded-lg border border-gray-200 dark:border-gray-700"
            >
              {editingId === shoe.id ? (
                <div className="space-y-3">
                  <div>
                    <label className="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-1">
                      Brand *
                    </label>
                    <input
                      type="text"
                      value={editShoe.brand || ''}
                      onChange={(e) => setEditShoe({ ...editShoe, brand: e.target.value })}
                      className="w-full px-3 py-2 border border-gray-300 dark:border-gray-600 rounded-lg bg-white dark:bg-gray-800 text-gray-900 dark:text-gray-100"
                    />
                  </div>
                  <div>
                    <label className="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-1">
                      Model *
                    </label>
                    <input
                      type="text"
                      value={editShoe.model || ''}
                      onChange={(e) => setEditShoe({ ...editShoe, model: e.target.value })}
                      className="w-full px-3 py-2 border border-gray-300 dark:border-gray-600 rounded-lg bg-white dark:bg-gray-800 text-gray-900 dark:text-gray-100"
                    />
                  </div>
                  <div>
                    <label className="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-1">
                      Initial Mileage
                    </label>
                    <input
                      type="number"
                      step="0.1"
                      value={editShoeInitialMileageInput}
                      onChange={(e) => setEditShoeInitialMileageInput(e.target.value)}
                      className="w-full px-3 py-2 border border-gray-300 dark:border-gray-600 rounded-lg bg-white dark:bg-gray-800 text-gray-900 dark:text-gray-100"
                      placeholder={unitPreference === 'imperial' ? 'Miles' : 'Kilometers'}
                    />
                    <p className="text-xs text-gray-500 dark:text-gray-400 mt-1">
                      Enter mileage in {unitPreference === 'imperial' ? 'miles' : 'kilometers'}
                    </p>
                  </div>
                  <div className="flex gap-2">
                    <button
                      onClick={() => handleUpdate(shoe.id)}
                      disabled={!editShoe.brand?.trim() || !editShoe.model?.trim() || updateMutation.isPending}
                      className="px-4 py-2 bg-blue-600 text-white rounded-lg hover:bg-blue-700 dark:bg-blue-500 dark:hover:bg-blue-600 disabled:bg-gray-400 disabled:cursor-not-allowed"
                    >
                      {updateMutation.isPending ? 'Saving...' : 'Save'}
                    </button>
                    <button
                      onClick={cancelEdit}
                      className="px-4 py-2 bg-gray-200 text-gray-700 rounded-lg hover:bg-gray-300 dark:bg-gray-700 dark:text-gray-300 dark:hover:bg-gray-600"
                    >
                      Cancel
                    </button>
                  </div>
                </div>
              ) : (
                <div>
                  <div className="flex items-start justify-between mb-2">
                    <div>
                      <h3 className="text-lg font-semibold text-gray-900 dark:text-gray-100">
                        {shoe.brand} {shoe.model}
                      </h3>
                      <p className="text-sm text-gray-600 dark:text-gray-400">
                        Total: {shoe.totalMileage.toFixed(1)} {shoe.unit}
                      </p>
                      {defaultShoe?.defaultShoeId === shoe.id && (
                        <span className="inline-block mt-1 px-2 py-1 text-xs bg-blue-100 text-blue-800 dark:bg-blue-900 dark:text-blue-200 rounded">
                          Default
                        </span>
                      )}
                    </div>
                    <div className="flex gap-2">
                      <button
                        onClick={() => startEdit(shoe)}
                        className="px-3 py-1 text-sm bg-gray-200 text-gray-700 rounded hover:bg-gray-300 dark:bg-gray-700 dark:text-gray-300 dark:hover:bg-gray-600"
                      >
                        Edit
                      </button>
                      <button
                        onClick={() => handleDelete(shoe.id)}
                        disabled={deleteMutation.isPending}
                        className="px-3 py-1 text-sm bg-red-200 text-red-700 rounded hover:bg-red-300 dark:bg-red-900 dark:text-red-200 dark:hover:bg-red-800 disabled:bg-gray-400 disabled:cursor-not-allowed"
                      >
                        {deleteMutation.isPending ? 'Deleting...' : 'Delete'}
                      </button>
                    </div>
                  </div>
                </div>
              )}
            </div>
          ))
        )}
      </div>
    </div>
  );
}


'use client';

import { useState, useEffect } from 'react';
import { useRouter } from 'next/navigation';
import { useAuth } from '@/contexts/AuthContext';
import * as api from '@/lib/api';

export default function LoginPage() {
  const [username, setUsername] = useState('');
  const [password, setPassword] = useState('');
  const [isRegistering, setIsRegistering] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [isLoading, setIsLoading] = useState(false);
  const [registrationAvailable, setRegistrationAvailable] = useState(false);
  const { login, register, isAuthenticated } = useAuth();
  const router = useRouter();

  useEffect(() => {
    // If already authenticated, redirect to dashboard
    if (isAuthenticated) {
      router.push('/dashboard');
      return;
    }

    // Check if registration is available
    const checkRegistration = async () => {
      try {
        const response = await api.checkRegistrationAvailable();
        setRegistrationAvailable(response.registrationAvailable);
        if (response.registrationAvailable) {
          setIsRegistering(true);
        }
      } catch (error) {
        console.error('Failed to check registration availability:', error);
      }
    };

    checkRegistration();
  }, [isAuthenticated, router]);

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setError(null);
    setIsLoading(true);

    try {
      if (isRegistering) {
        await register(username, password);
      } else {
        await login(username, password);
      }
      // Navigation is handled by the auth context
    } catch (err: any) {
      setError(err.message || 'An error occurred. Please try again.');
    } finally {
      setIsLoading(false);
    }
  };

  return (
    <div className="min-h-screen flex items-center justify-center bg-gray-50 py-12 px-4 sm:px-6 lg:px-8">
      <div className="max-w-md w-full space-y-8">
        <div>
          <h2 className="mt-6 text-center text-3xl font-extrabold text-gray-900">
            {isRegistering ? 'Create Account' : 'Sign in to Tempo'}
          </h2>
          <p className="mt-2 text-center text-sm text-gray-600">
            {isRegistering
              ? 'Create your account to get started'
              : 'Enter your credentials to access your workouts'}
          </p>
        </div>
        <form className="mt-8 space-y-6" onSubmit={handleSubmit}>
          {error && (
            <div className="rounded-md bg-red-50 p-4">
              <div className="text-sm text-red-800">{error}</div>
            </div>
          )}
          <div className="rounded-md shadow-sm -space-y-px">
            <div>
              <label htmlFor="username" className="sr-only">
                Username
              </label>
              <input
                id="username"
                name="username"
                type="text"
                required
                className="appearance-none rounded-none relative block w-full px-3 py-2 border border-gray-300 placeholder-gray-500 text-gray-900 rounded-t-md focus:outline-none focus:ring-indigo-500 focus:border-indigo-500 focus:z-10 sm:text-sm"
                placeholder="Username"
                value={username}
                onChange={(e) => setUsername(e.target.value)}
                disabled={isLoading}
              />
            </div>
            <div>
              <label htmlFor="password" className="sr-only">
                Password
              </label>
              <input
                id="password"
                name="password"
                type="password"
                required
                className="appearance-none rounded-none relative block w-full px-3 py-2 border border-gray-300 placeholder-gray-500 text-gray-900 rounded-b-md focus:outline-none focus:ring-indigo-500 focus:border-indigo-500 focus:z-10 sm:text-sm"
                placeholder="Password"
                value={password}
                onChange={(e) => setPassword(e.target.value)}
                disabled={isLoading}
                minLength={6}
              />
            </div>
          </div>

          <div>
            <button
              type="submit"
              disabled={isLoading}
              className="group relative w-full flex justify-center py-2 px-4 border border-transparent text-sm font-medium rounded-md text-white bg-indigo-600 hover:bg-indigo-700 focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-indigo-500 disabled:opacity-50 disabled:cursor-not-allowed"
            >
              {isLoading ? 'Please wait...' : isRegistering ? 'Create Account' : 'Sign in'}
            </button>
          </div>

          {!isRegistering && registrationAvailable && (
            <div className="text-center">
              <button
                type="button"
                onClick={() => setIsRegistering(true)}
                className="text-sm text-indigo-600 hover:text-indigo-500"
              >
                Don't have an account? Register
              </button>
            </div>
          )}

          {isRegistering && !registrationAvailable && (
            <div className="rounded-md bg-yellow-50 p-4">
              <div className="text-sm text-yellow-800">
                Registration is disabled. An account already exists.
              </div>
            </div>
          )}
        </form>
      </div>
    </div>
  );
}


'use client';

import { useState, useEffect } from 'react';
import { useRouter } from 'next/navigation';
import { useAuth } from '@/contexts/AuthContext';
import * as api from '@/lib/api';
import { getPasswordLengthAndBytesError } from '@/lib/passwordPolicy';

export default function LoginPage() {
  const [username, setUsername] = useState('');
  const [password, setPassword] = useState('');
  const [confirmPassword, setConfirmPassword] = useState('');
  const [rememberMe, setRememberMe] = useState(false);
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

    if (isRegistering) {
      const pwdErr = getPasswordLengthAndBytesError(password);
      if (pwdErr) {
        setError(pwdErr);
        return;
      }
      if (password !== confirmPassword) {
        setError('Passwords do not match');
        return;
      }
    }

    setIsLoading(true);

    try {
      if (isRegistering) {
        await register(username, password);
      } else {
        await login(username, password, rememberMe);
      }
      // Navigation is handled by the auth context
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : 'An error occurred. Please try again.');
    } finally {
      setIsLoading(false);
    }
  };

  return (
    <div className="flex min-h-screen items-center justify-center bg-zinc-50 dark:bg-black py-16 px-8">
      <div className="w-full max-w-md">
        <div className="bg-white dark:bg-gray-900 p-8 rounded-lg border border-gray-200 dark:border-gray-800 space-y-8">
          <div>
            <h2 className="text-center text-3xl font-extrabold text-gray-900 dark:text-gray-100">
              {isRegistering ? 'Create Account' : 'Sign in to Tempo'}
            </h2>
            <p className="mt-2 text-center text-sm text-gray-600 dark:text-gray-400">
              {isRegistering
                ? 'Create your account to get started'
                : 'Enter your credentials to access your workouts'}
            </p>
            {isRegistering && (
              <p className="text-center text-xs text-gray-500 dark:text-gray-400">
                Use a memorable passphrase, 16–64 characters. Spaces and Unicode are fine; no required symbol or
                digit rules.
              </p>
            )}
          </div>
          <form className="space-y-6" onSubmit={handleSubmit}>
            {error && (
              <div className="rounded-lg bg-red-50 dark:bg-red-900/20 border border-red-200 dark:border-red-800 p-4">
                <div className="text-sm text-red-800 dark:text-red-200">{error}</div>
              </div>
            )}
            <div className="space-y-4">
              <div>
                <label htmlFor="username" className="sr-only">
                  Username
                </label>
                <input
                  id="username"
                  name="username"
                  type="text"
                  required
                  className="appearance-none relative block w-full px-3 py-2 border border-gray-300 dark:border-gray-600 rounded-lg bg-white dark:bg-gray-800 placeholder-gray-500 dark:placeholder-gray-400 text-gray-900 dark:text-gray-100 focus:outline-none focus:ring-2 focus:ring-blue-500 focus:border-blue-500 sm:text-sm"
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
                  className="appearance-none relative block w-full px-3 py-2 border border-gray-300 dark:border-gray-600 rounded-lg bg-white dark:bg-gray-800 placeholder-gray-500 dark:placeholder-gray-400 text-gray-900 dark:text-gray-100 focus:outline-none focus:ring-2 focus:ring-blue-500 focus:border-blue-500 sm:text-sm"
                  placeholder="Password"
                  value={password}
                  onChange={(e) => setPassword(e.target.value)}
                  disabled={isLoading}
                  minLength={isRegistering ? 16 : undefined}
                  maxLength={isRegistering ? 64 : undefined}
                />
              </div>
              {isRegistering && (
                <div>
                  <label htmlFor="confirmPassword" className="sr-only">
                    Confirm Password
                  </label>
                  <input
                    id="confirmPassword"
                    name="confirmPassword"
                    type="password"
                    required
                    className="appearance-none relative block w-full px-3 py-2 border border-gray-300 dark:border-gray-600 rounded-lg bg-white dark:bg-gray-800 placeholder-gray-500 dark:placeholder-gray-400 text-gray-900 dark:text-gray-100 focus:outline-none focus:ring-2 focus:ring-blue-500 focus:border-blue-500 sm:text-sm"
                    placeholder="Confirm Password"
                    value={confirmPassword}
                    onChange={(e) => setConfirmPassword(e.target.value)}
                    disabled={isLoading}
                    minLength={16}
                    maxLength={64}
                  />
                </div>
              )}
              {!isRegistering && (
                <div className="flex items-center">
                  <input
                    id="rememberMe"
                    name="rememberMe"
                    type="checkbox"
                    checked={rememberMe}
                    onChange={(e) => setRememberMe(e.target.checked)}
                    disabled={isLoading}
                    className="h-4 w-4 text-blue-600 focus:ring-blue-500 border-gray-300 dark:border-gray-600 rounded bg-white dark:bg-gray-800"
                  />
                  <label htmlFor="rememberMe" className="ml-2 block text-sm text-gray-900 dark:text-gray-100">
                    Remember me
                  </label>
                </div>
              )}
            </div>

            <div>
              <button
                type="submit"
                disabled={isLoading}
                className="w-full flex justify-center py-2 px-4 border border-transparent text-sm font-medium rounded-lg text-white bg-blue-600 hover:bg-blue-700 dark:bg-blue-500 dark:hover:bg-blue-600 focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-blue-500 disabled:opacity-50 disabled:cursor-not-allowed transition-colors"
              >
                {isLoading ? 'Please wait...' : isRegistering ? 'Create Account' : 'Sign in'}
              </button>
            </div>

            {!isRegistering && registrationAvailable && (
              <div className="text-center">
                <button
                  type="button"
                  onClick={() => {
                    setIsRegistering(true);
                    setPassword('');
                    setConfirmPassword('');
                  }}
                  className="text-sm text-blue-600 dark:text-blue-400 hover:text-blue-500 dark:hover:text-blue-300"
                >
                  Do not have an account? Register
                </button>
              </div>
            )}

            {isRegistering && (
              <div className="text-center">
                <button
                  type="button"
                  onClick={() => {
                    setIsRegistering(false);
                    setPassword('');
                    setConfirmPassword('');
                  }}
                  className="text-sm text-blue-600 dark:text-blue-400 hover:text-blue-500 dark:hover:text-blue-300"
                >
                  Already have an account? Sign in
                </button>
              </div>
            )}

            {isRegistering && !registrationAvailable && (
              <div className="rounded-lg bg-yellow-50 dark:bg-yellow-900/20 border border-yellow-200 dark:border-yellow-800 p-4">
                <div className="text-sm text-yellow-800 dark:text-yellow-200">
                  Registration is disabled. An account already exists.
                </div>
              </div>
            )}
          </form>
        </div>
      </div>
    </div>
  );
}


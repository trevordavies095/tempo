'use client';

import { useState, useEffect } from 'react';
import { useRouter } from 'next/navigation';
import { useAuth } from '@/contexts/AuthContext';
import * as api from '@/lib/api';
import {
  PASSWORD_MAX_LENGTH,
  PASSWORD_MIN_LENGTH,
  getPasswordLengthAndBytesError,
} from '@/lib/passwordPolicy';
import { PageShell } from '@/components/ui/PageShell';
import { Card } from '@/components/ui/Card';
import { Button } from '@/components/ui/Button';

const fieldClass =
  'appearance-none relative block w-full px-3 py-2 border border-border rounded-tempo bg-raised placeholder:text-muted text-ink focus:outline-none focus:ring-2 focus:ring-volt sm:text-sm';

export default function LoginPage() {
  const [username, setUsername] = useState('');
  const [password, setPassword] = useState('');
  const [confirmPassword, setConfirmPassword] = useState('');
  const [rememberMe, setRememberMe] = useState(false);
  const [isRegistering, setIsRegistering] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [isLoading, setIsLoading] = useState(false);
  const [registrationAvailable, setRegistrationAvailable] = useState(false);
  const { login, register, isAuthenticated, user } = useAuth();
  const router = useRouter();

  useEffect(() => {
    // If already authenticated, redirect based on onboarding state
    if (isAuthenticated && user) {
      router.push(user.onboardingCompleted ? '/dashboard' : '/onboarding');
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

    if (!isAuthenticated) {
      checkRegistration();
    }
  }, [isAuthenticated, user, router]);

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
    <PageShell
      density="control"
      centered
      title={isRegistering ? 'Create Account' : 'Sign in to Tempo'}
      subtitle={
        isRegistering
          ? 'Create your account to get started'
          : 'Enter your credentials to access your workouts'
      }
    >
      <Card className="w-full max-w-md space-y-6">
        {isRegistering && (
          <p className="text-xs text-muted">
            Use a memorable passphrase, {PASSWORD_MIN_LENGTH}–{PASSWORD_MAX_LENGTH} characters. Spaces and
            Unicode are fine; no required symbol or digit rules.
          </p>
        )}
        <form className="space-y-6" onSubmit={handleSubmit}>
          {error && (
            <div className="rounded-tempo border border-danger/40 bg-canvas p-4">
              <div className="text-sm text-danger">{error}</div>
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
                className={fieldClass}
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
                className={fieldClass}
                placeholder="Password"
                value={password}
                onChange={(e) => setPassword(e.target.value)}
                disabled={isLoading}
                minLength={isRegistering ? PASSWORD_MIN_LENGTH : undefined}
                maxLength={isRegistering ? PASSWORD_MAX_LENGTH : undefined}
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
                  className={fieldClass}
                  placeholder="Confirm Password"
                  value={confirmPassword}
                  onChange={(e) => setConfirmPassword(e.target.value)}
                  disabled={isLoading}
                  minLength={PASSWORD_MIN_LENGTH}
                  maxLength={PASSWORD_MAX_LENGTH}
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
                  className="h-4 w-4 accent-[var(--volt)] border-border rounded bg-raised"
                />
                <label htmlFor="rememberMe" className="ml-2 block text-sm text-ink">
                  Remember me
                </label>
              </div>
            )}
          </div>

          <Button type="submit" disabled={isLoading} className="w-full">
            {isLoading ? 'Please wait...' : isRegistering ? 'Create Account' : 'Sign in'}
          </Button>

          {!isRegistering && registrationAvailable && (
            <div className="text-center">
              <Button
                type="button"
                variant="ghost"
                onClick={() => {
                  setIsRegistering(true);
                  setPassword('');
                  setConfirmPassword('');
                }}
              >
                Do not have an account? Register
              </Button>
            </div>
          )}

          {isRegistering && (
            <div className="text-center">
              <Button
                type="button"
                variant="ghost"
                onClick={() => {
                  setIsRegistering(false);
                  setPassword('');
                  setConfirmPassword('');
                }}
              >
                Already have an account? Sign in
              </Button>
            </div>
          )}

          {isRegistering && !registrationAvailable && (
            <div className="rounded-tempo border border-border bg-canvas p-4">
              <div className="text-sm text-muted">
                Registration is disabled. An account already exists.
              </div>
            </div>
          )}
        </form>
      </Card>
    </PageShell>
  );
}

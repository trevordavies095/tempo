'use client';

import { createContext, useContext, useEffect, useState, ReactNode } from 'react';
import { useRouter } from 'next/navigation';
import * as api from '@/lib/api';

interface User {
  userId: string;
  username: string;
  createdAt: string;
  lastLoginAt: string | null;
  onboardingCompleted: boolean;
}

interface AuthContextType {
  user: User | null;
  isAuthenticated: boolean;
  isLoading: boolean;
  login: (username: string, password: string, rememberMe?: boolean) => Promise<void>;
  logout: () => Promise<void>;
  register: (username: string, password: string) => Promise<void>;
  checkAuth: () => Promise<void>;
  completeOnboarding: () => Promise<void>;
}

const AuthContext = createContext<AuthContextType | undefined>(undefined);

function postAuthPath(user: User): string {
  return user.onboardingCompleted ? '/dashboard' : '/onboarding';
}

export function AuthProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<User | null>(null);
  const [isLoading, setIsLoading] = useState(true);
  const router = useRouter();

  const checkAuth = async () => {
    try {
      const userInfo = await api.getCurrentUser();
      setUser(userInfo);
    } catch (error) {
      setUser(null);
    } finally {
      setIsLoading(false);
    }
  };

  useEffect(() => {
    checkAuth();
  }, []);

  // Register global 401 error handler
  useEffect(() => {
    const handleAuthError = () => {
      setUser(null);
      setIsLoading(false);
    };

    api.setAuthErrorHandler(handleAuthError);

    // Cleanup: unregister handler on unmount
    return () => {
      api.setAuthErrorHandler(null);
    };
  }, []);

  const login = async (username: string, password: string, rememberMe?: boolean) => {
    try {
      await api.login(username, password, rememberMe);
      const userInfo = await api.getCurrentUser();
      setUser(userInfo);
      setIsLoading(false);
      router.push(postAuthPath(userInfo));
    } catch (error) {
      throw error;
    }
  };

  const logout = async () => {
    try {
      await api.logout();
      setUser(null);
      router.push('/login');
    } catch (error) {
      // Even if logout fails, clear local state
      setUser(null);
      router.push('/login');
    }
  };

  const register = async (username: string, password: string) => {
    try {
      await api.register(username, password);
      // After registration, automatically log in
      await login(username, password);
    } catch (error) {
      throw error;
    }
  };

  const completeOnboarding = async () => {
    const userInfo = await api.completeOnboarding();
    setUser(userInfo);
    router.push('/dashboard');
  };

  const value: AuthContextType = {
    user,
    isAuthenticated: !!user,
    isLoading,
    login,
    logout,
    register,
    checkAuth,
    completeOnboarding,
  };

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

export function useAuth() {
  const context = useContext(AuthContext);
  if (context === undefined) {
    throw new Error('useAuth must be used within an AuthProvider');
  }
  return context;
}

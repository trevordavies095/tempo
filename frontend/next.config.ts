import type { NextConfig } from "next";

const nextConfig: NextConfig = {
  output: 'standalone',
  // Next.js 16.3+ writes AGENTS.md / CLAUDE.md by default; keep the repo free of that noise.
  agentRules: false,
  async rewrites() {
    // Use Docker service name in production, localhost in development
    // Check if we're in Docker by looking for the service name or use env var
    const apiUrl = process.env.API_SERVICE_URL || 
                   (process.env.NODE_ENV === 'production' ? 'http://api:5001' : 'http://localhost:5001');
    
    return [
      {
        source: '/api/:path*',
        destination: `${apiUrl}/:path*`,
      },
    ];
  },
};

export default nextConfig;

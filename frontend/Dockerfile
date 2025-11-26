# Build stage
FROM node:20-alpine AS build
WORKDIR /app

# Build arguments for version metadata and environment variables
ARG APP_VERSION=1.0.0
ARG BUILD_DATE
ARG GIT_COMMIT
ARG NEXT_PUBLIC_API_URL=http://localhost:5001

# Copy package files
COPY package*.json ./

# Install dependencies
RUN npm ci --legacy-peer-deps

# Copy source code
COPY . .

# Set environment variables
ENV NEXT_PUBLIC_API_URL=$NEXT_PUBLIC_API_URL
ENV NEXT_PUBLIC_VERSION=$APP_VERSION
ENV NEXT_PUBLIC_BUILD_DATE=$BUILD_DATE
ENV NEXT_PUBLIC_GIT_COMMIT=$GIT_COMMIT

# Build Next.js application
RUN npm run build

# Production stage
FROM node:20-alpine AS runner
WORKDIR /app

# Build arguments for version metadata
ARG APP_VERSION=1.0.0
ARG BUILD_DATE
ARG GIT_COMMIT

ENV NODE_ENV=production

# Copy necessary files from build
COPY --from=build /app/public ./public
COPY --from=build /app/.next/standalone ./
COPY --from=build /app/.next/static ./.next/static

# Add version metadata labels
LABEL org.opencontainers.image.version="${APP_VERSION}" \
      org.opencontainers.image.created="${BUILD_DATE}" \
      org.opencontainers.image.revision="${GIT_COMMIT}" \
      org.opencontainers.image.title="Tempo Frontend"

# Set environment variables
ENV PORT=3000
ENV HOSTNAME="0.0.0.0"
ENV TEMPO_VERSION="${APP_VERSION}"
ENV TEMPO_BUILD_DATE="${BUILD_DATE}"
ENV TEMPO_GIT_COMMIT="${GIT_COMMIT}"

# Expose port
EXPOSE 3000

# Run the app
CMD ["node", "server.js"]


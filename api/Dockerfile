# Build stage
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Build arguments for version metadata
# Use APP_VERSION instead of VERSION to avoid MSBuild conflicts
ARG APP_VERSION=1.0.0
ARG BUILD_DATE
ARG GIT_COMMIT

# Copy project file and restore dependencies
COPY Tempo.Api.csproj .
RUN dotnet restore

# Copy everything else and build
COPY . .

# Ensure VERSION file exists (create from APP_VERSION if not present)
# This ensures the file is always available for the final stage
RUN if [ ! -f VERSION ]; then \
      echo "${APP_VERSION}" > VERSION; \
    fi

RUN dotnet build -c Release -o /app/build

# Publish stage
FROM build AS publish
RUN dotnet publish -c Release -o /app/publish

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app

# Build arguments for version metadata (passed from build stage)
ARG APP_VERSION=1.0.0
ARG BUILD_DATE
ARG GIT_COMMIT

# Install curl for healthcheck
RUN apt-get update && apt-get install -y curl && rm -rf /var/lib/apt/lists/*

# Copy published app
COPY --from=publish /app/publish .

# Create media directory
RUN mkdir -p /app/media

# Copy VERSION file as fallback (ensured by build workflow or created from APP_VERSION)
# This allows VersionEndpoints.cs to read from file if env vars are not set
COPY --from=build /src/VERSION /app/VERSION

# Add version metadata labels
LABEL org.opencontainers.image.version="${APP_VERSION}" \
      org.opencontainers.image.created="${BUILD_DATE}" \
      org.opencontainers.image.revision="${GIT_COMMIT}" \
      org.opencontainers.image.title="Tempo API"

# Set environment variables
ENV ASPNETCORE_URLS=http://+:5001
ENV TEMPO_VERSION="${APP_VERSION}"
ENV TEMPO_BUILD_DATE="${BUILD_DATE}"
ENV TEMPO_GIT_COMMIT="${GIT_COMMIT}"

# Expose port
EXPOSE 5001

# Run the app
ENTRYPOINT ["dotnet", "Tempo.Api.dll"]


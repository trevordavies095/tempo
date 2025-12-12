# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [2.0.1] - 2025-12-11

### Security
- **Fixed React Server Components vulnerabilities**
  - Updated React from 19.2.1 to 19.2.3 to address security vulnerabilities
  - Fixed CVE-2025-55184 and CVE-2025-67779: Denial of Service (High - CVSS 7.5)
  - Fixed CVE-2025-55183: Source Code Exposure (Medium - CVSS 5.3)
  - Updated Next.js to latest 16.x version with security patches
  - All users should update immediately by running `npm install` in the frontend directory

## [2.0.0] - 2025-12-06

### Changed
- **API endpoint restructuring for mobile development**
  - Extracted stats endpoints to separate `/stats` group for better organization
  - Moved recalculation actions from `/settings` to `/workouts` for logical grouping
  - Created new `StatsEndpoints.cs` with 8 stats endpoints
  - Improved API organization for mobile app development

### Breaking Changes
- **Endpoint URL changes** (functionality remains identical):
  - Stats endpoints moved: `/workouts/stats/*` → `/stats/*`
    - `/workouts/stats/weekly` → `/stats/weekly`
    - `/workouts/stats/yearly` → `/stats/yearly`
    - `/workouts/stats/relative-effort` → `/stats/relative-effort`
    - `/workouts/stats/yearly-weekly` → `/stats/yearly-weekly`
    - `/workouts/stats/available-periods` → `/stats/available-periods`
    - `/workouts/stats/available-years` → `/stats/available-years`
    - `/workouts/stats/best-efforts` → `/stats/best-efforts`
    - `/workouts/stats/best-efforts/recalculate` → `/stats/best-efforts/recalculate`
  - Recalculation endpoints moved: `/settings/recalculate-*` → `/workouts/recalculate-*`
    - `/settings/recalculate-relative-effort/count` → `/workouts/recalculate-relative-effort/count`
    - `/settings/recalculate-relative-effort` → `/workouts/recalculate-relative-effort`
    - `/settings/recalculate-splits/count` → `/workouts/recalculate-splits/count`
    - `/settings/recalculate-splits` → `/workouts/recalculate-splits`
- **Frontend updated**: All frontend API calls have been updated to use new paths
- **API consumers**: Mobile apps or other API clients must update to new endpoint paths

### Technical
- New `StatsEndpoints.cs` file with dedicated stats endpoint group
- Updated `WorkoutsEndpoints.cs` to include recalculation actions
- Updated `SettingsEndpoints.cs` to contain only configuration endpoints
- Updated API reference documentation
- Updated Bruno test collection with new endpoint paths

## [1.4.0] - 2025-12-04

### Added
- **Best efforts tracking and visualization**
  - Track personal best efforts across common distances
  - New Best Efforts chart on the dashboard for visualizing performance over time
  - Backend support for storing and querying best effort records
- **Shoe tracking**
  - Manage running shoes, including name, brand, and initial mileage
  - Assign shoes to workouts and automatically track cumulative mileage
  - Shoe selection integrated into workout import flow and workout editing
  - Settings page section for creating, updating, and deactivating shoes
- **Complete data export functionality**
  - Export all Tempo data (workouts, routes, splits, time series, media metadata, settings) to a portable archive
  - Includes a manifest file and JSON representations of all core entities
  - Designed for backup, migration, and offline analysis use cases
- **Tempo export import functionality**
  - Import a Tempo export archive into another Tempo instance
  - Validates manifest structure, raw file references, and shoe references
  - Supports merging data into an existing installation while avoiding duplicates
- **Settings layout improvements**
  - Reorganized settings screen into logical sections (units, heart rate zones, export/import, shoes, etc.)
  - New UI components for export/import and shoe management

## [1.3.0] - 2025-11-30

### Added
- **JWT-based authentication system** - Complete authentication system for securing the application
  - User registration (single-user deployment pattern - registration locked after first user)
  - Login/logout functionality with JWT tokens stored in httpOnly cookies
  - Password hashing using BCrypt for secure password storage
  - Authentication endpoints: `/auth/register`, `/auth/login`, `/auth/logout`, `/auth/me`, `/auth/registration-available`
  - Frontend authentication context and AuthGuard component for protected routes
  - Login/registration pages with improved UX (password confirmation, form validation)
  - All workout and settings endpoints now require authentication (except `/health` and `/version`)
  - JWT secret key validation on startup (prevents deployment with default placeholder)
  - Serializable database transactions to prevent race conditions during registration
- **Comprehensive documentation site** - Full MkDocs documentation deployed to GitHub Pages
  - Complete documentation covering getting started, user guides, developer docs, deployment, and troubleshooting
  - Automated deployment workflow for GitHub Pages
  - Material for MkDocs theme with search, navigation, and code highlighting
  - Guides for installation, configuration, API reference, security best practices, and more
- Bruno API testing collection - Comprehensive interactive API testing collection with 30+ test requests covering all endpoints
  - Organized by endpoint groups (Workouts, Settings, Version, Health, Authentication)
  - Environment configuration for local development
  - Enables API testing and exploration without requiring the frontend
  - Test files for all CRUD operations, imports, stats, media, configuration, and authentication endpoints
  - Updated to handle authentication with JWT tokens

### Changed
- Refactored API endpoints from inline lambdas to private static methods with XML documentation
  - Improves code organization and maintainability
  - Enables better Swagger documentation integration
  - All endpoint behavior remains unchanged (backward compatible)
- Enhanced API documentation with XML comments
  - Enabled XML documentation generation in project
  - Configured Swagger to include XML comments for improved API docs
  - Added comprehensive parameter and return type documentation
- **Security improvements**
  - JWT secret key must be configured in production (startup fails with default placeholder)
  - Username trimming consistency between registration and login
  - JWT expiration configuration consistency fixes
  - Added credentials to media API calls for authenticated requests

### Fixed
- Minor frontend component updates (RelativeEffortGraph, WorkoutMap)
- Fixed username trimming mismatch between Register and Login endpoints
- Fixed JWT expiration inconsistency in Login endpoint
- Fixed JWT secret key validation bypass in docker-compose.prod.yml
- Fixed race condition in user registration using serializable transactions

## [1.2.0] - 2025-01-27

### Added
- Workout crop/trim functionality - Users can now crop workouts by removing time from the beginning and/or end
  - Interactive dialog with time input fields for start and end trim values
  - Preview of new duration and distance before applying changes
  - Automatically recalculates all derived data (splits, pace, elevation, heart rate stats, relative effort)
  - Preserves original raw data for audit trail
  - Updates route coordinates, time series data, and workout aggregates
  - Accessible from workout detail page via crop button
- Activity name editing - Users can now edit workout activity names through inline editing
  - Click on the activity name in the workout detail header to edit
  - Inline editing with keyboard shortcuts (Enter to save, Escape to cancel)
  - Name field supports up to 200 characters
  - Changes are automatically saved and reflected across all views

## [1.1.2] - 2025-11-27

### Fixed
- Fixed database migration errors causing container startup failures
- Made all migrations idempotent to prevent "already exists" errors
- Enhanced DatabaseMigrationHelper to detect existing tables and columns
- Prevents migration conflicts when database state doesn't match migration history

## [1.1.1] - 2025-11-27

### Fixed
- Bug fixes (patch release)

## [1.1.0] - 2025-11-26

### Changed
- API refactor - comprehensive code refactoring to reduce duplication and complexity (~800+ lines eliminated, new utilities/services created)
- Frontend refactor - reduce code duplication and improve maintainability (764 lines removed, new hooks/components extracted)

### Fixed
- Recalculate splits when unit preference changes (fixes #18)
- Invalidate cache after Relative Effort recalculation

## [1.0.0] - 2025-11-26

### Added
- Initial release of Tempo running tracker
- Support for importing GPX, FIT, and Strava CSV workout files
- Workout analytics with distance, pace, elevation, and heart rate tracking
- Interactive maps with route visualization
- Media support for photos and videos attached to workouts
- Weather data integration for workout conditions
- Bulk import functionality for Strava exports
- Heart rate zone calculations (Age-based, Karvonen, Custom)
- Relative effort calculations
- Weekly and yearly statistics dashboards


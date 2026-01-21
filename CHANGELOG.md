# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [2.2.0] - 2026-01-21

### Added
- **Route matching and comparison**
  - New `RouteMatchingService` for finding similar routes across workouts
  - Find workouts that follow similar paths based on route geometry comparison
  - Route comparison tab in workout detail view showing similar routes
  - Similar routes section displaying matches with similarity scores, distance differences, and time comparisons
  - Automatic route matching when viewing workout details
  - GIN index on `WorkoutRoutes.RouteGeoJson` for efficient route queries
  - Configurable similarity thresholds (start/end proximity, distance similarity, route similarity)
- **Remember Me authentication**
  - "Remember me" checkbox on login page for extended session duration
  - JWT tokens with 30-day expiration when "Remember me" is enabled (vs. 7 days default)
  - Configurable via `JWT:RememberMeExpirationDays` setting (default: 30 days)
  - Enhanced authentication context with remember me support
- **Tabler Icons integration**
  - Replaced all custom SVG icons with Tabler Icons (`@tabler/icons-react`)
  - Consistent icon styling across the application
  - Improved icon accessibility and maintainability
  - Icons used in: ActivitiesTable, BestEffortsChart, MediaUpload, TempoExportImport, UnitPreferenceSection, and more

### Fixed
- **Identical split times bug**
  - Fixed split recalculation producing identical times across all splits
  - Enhanced `FitParserService` to store track points with timestamps in `RawFitData`
  - Improved `SplitRecalculationService` with better timestamp extraction from raw data
  - Enhanced `BulkImportService` to update duplicate workouts with complete track point data
  - Re-importing workouts now properly updates split data with accurate timestamps
  
  **Important for affected users:** If you experienced the identical split times issue (where all splits showed the same or nearly the same pace), you may need to re-import affected workouts to fix the split data. Workouts imported after this fix will automatically have correct split calculations. For existing workouts, re-importing the original workout files (GPX, FIT, or CSV) will update the stored data with complete track point information, enabling accurate split recalculation. The bulk import feature will automatically update duplicate workouts when re-importing.
- **Pace calculation precision**
  - Changed `Workout.AvgPaceS` and `WorkoutSplit.PaceS` from `int` to `double` for fractional second precision
  - Prevents rounding errors that made splits appear more similar than they were
  - Improved pace formatting with proper rounding in frontend
  - Database migration automatically converts existing data
- **Weather service date parsing**
  - Fixed date parsing issues in `WeatherService` using culture-aware parsing
  - Added `DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal` for consistent UTC handling
  - Improved reliability when fetching weather data from Open-Meteo API
- **Stats endpoint date parsing**
  - Fixed date parsing in stats endpoints using culture-aware parsing
  - Consistent date handling across all stats queries
- **Test reliability improvements**
  - Sequential test execution to prevent database state conflicts
  - Fixed authentication race conditions in test setup using `SemaphoreSlim`
  - Fixed flaky JWT expiration tests with proper token generation
  - Enhanced test logging with detailed verbosity in CI
  - Increased JWT clock skew tolerance from 0 to 5 minutes for better reliability

### Changed
- **FIT parser enhancements**
  - `RawFitData` now includes complete track point data (lat, lon, elevation, time, heart rate, cadence, power, temperature)
  - Enables accurate split recalculation for FIT files
  - Backward compatible with existing FIT files (metadata-only format still supported)
- **Pace formatting improvements**
  - Improved pace rounding in `formatPace` function
  - Better handling of pace conversion between metric and imperial units
  - More accurate pace display with proper second rounding
- **Authentication error handling**
  - Global 401 error handler in `AuthContext` for automatic logout on authentication failures
  - Improved error handling for expired tokens and authentication errors
  - Better user experience when sessions expire

### Technical
- Database migration: `ChangePaceFieldsToDouble` - converts pace fields from integer to double precision
- Database migration: `AddGinIndexToWorkoutRouteGeoJson` - adds GIN index for efficient route queries
- Updated `RouteMatchingService` with configurable similarity thresholds and efficient route comparison algorithms
- Enhanced test infrastructure with sequential execution configuration
- Updated `TestHttpClientFactory` with thread-safe user creation
- Improved JWT validation with configurable clock skew tolerance
- Updated frontend dependencies: `@tabler/icons-react` added

### Migration Notes

**Identical Split Times Bug Fix:**
If you experienced the identical split times issue where all splits showed the same or nearly the same pace when using "Recalculate Splits", you may need to re-import affected workouts to fix the split data. This issue affected workouts imported before version 2.2.0.

**To fix affected workouts:**
1. Re-import the original workout files (GPX, FIT, or CSV) for workouts with incorrect split data
2. The bulk import feature will automatically detect duplicates and update them with complete track point information
3. After re-importing, split recalculation will work correctly with accurate timestamps

**Note:** New workouts imported after upgrading to 2.2.0 will automatically have correct split calculations and do not need to be re-imported.

## [2.1.1] - 2026-01-01

### Added
- **Automated testing suite**
  - Comprehensive test infrastructure with 29 test files covering unit and integration tests
  - CI integration with automated test execution and coverage reporting
  - 45% code coverage threshold enforced in CI (will be gradually increased)
  - Test infrastructure for authenticated requests, test data seeding, and database management
  - Full test coverage for core workflows: authentication, workouts, imports/exports, and statistics
  - Unit tests for all major services (parsers, export, import, media, calculations)
  - Integration tests for all API endpoints
  - GitHub Actions workflow for automated testing on push/PR to main and develop branches
  - Coverage reports generated and uploaded as artifacts

### Technical
- Added xUnit test framework with FluentAssertions and Moq
- Created test infrastructure: TempoWebApplicationFactory, TestHttpClientFactory, TestDataSeeder
- Support for SQLite in-memory database for fast test execution
- Test collections for proper test isolation
- Coverage configuration with appropriate exclusions (Program.cs, Migrations, FitSDK)
- Solution file (Tempo.sln) for managing API and test projects together

## [2.1.0] - 2025-12-14

### Added
- **Enhanced FIT file support with sensor data extraction**
  - Extract heart rate, cadence, power, and temperature from FIT RecordMesg messages
  - Create time-series records from FIT files with sensor data for detailed analysis
  - Backward compatible with FIT files that don't contain sensor data
  - Automatically calculate aggregate metrics (max/avg cadence, max/avg power) from time-series data
- **Additional FIT metrics extraction**
  - Extract speed, grade, and vertical speed from FIT files
  - Store speed, grade, and vertical speed in time-series records for detailed analysis
  - Enhanced speed calculation with fallback to standard speed when enhanced speed is unavailable
  - Validated and clamped grade values to prevent invalid data (range: -100% to 100%)
- **GPX TrackPointExtension support**
  - Extract heart rate, cadence, power, and temperature from GPX TrackPointExtension elements
  - Support for Garmin's TrackPointExtension namespace (`http://www.garmin.com/xmlschemas/TrackPointExtension/v1`)
  - Automatically calculate aggregate metrics from GPX sensor data
  - Enables full sensor data support for GPX files exported from Garmin devices
- **Enhanced time-series data**
  - Time-series records now support cadence (rpm), power (watts), and temperature (°C)
  - Speed, grade, and vertical speed metrics available in time-series for FIT files
  - Improved data validation to prevent NaN and Infinity values in time-series records
  - Better handling of missing or invalid sensor data

### Fixed
- **Version display in Docker builds**
  - Fixed System Information always showing version 1.0.0 for edge-tagged Docker images
  - Updated `docker-build.yml` workflow to pass `APP_VERSION`, `BUILD_DATE`, and `GIT_COMMIT` build arguments
  - Added VERSION file to Docker images as fallback mechanism
  - Edge images now correctly display the actual version (e.g., 2.0.1, 2.1.0)
- **Speed metric preservation**
  - Fixed `MaxSpeedMps` and `AvgSpeedMps` being overwritten during import
  - Preserve speed values from original data sources (Strava CSV, GPX calculated data)
  - Improved speed calculation consistency between GPX and FIT imports
- **Data validation improvements**
  - Added validation to reject negative and Infinity distance values in time-series
  - Prevent Infinity values from being clamped to valid grades
  - Improved NaN value handling in FIT record processing
  - Enhanced time-series validation logic for better data quality

### Changed
- **Improved FIT file processing**
  - Better handling of DateTime ambiguity in FIT time series
  - Enhanced validation logic for FIT time-series data
  - More robust error handling for FIT files with missing or incomplete data

### Technical
- Updated `WorkoutTimeSeries` model with new fields: `SpeedMps`, `GradePercent`, `VerticalSpeedMps`
- Enhanced `FitParserService` to extract additional metrics from FIT RecordMesg
- Enhanced `GpxParserService` to parse TrackPointExtension elements
- Updated `BulkImportService` and `WorkoutsEndpoints` to handle new sensor data fields
- Improved aggregate metric calculations from time-series data

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


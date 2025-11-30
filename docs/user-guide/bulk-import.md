# Bulk Import from Strava

Import your entire Strava history at once using a Strava data export ZIP file.

## Overview

The bulk import feature allows you to import multiple workouts from a Strava data export. This is useful when:
- Migrating from Strava to Tempo
- Importing your complete workout history
- Backing up and restoring workouts

## Requesting Your Strava Data Export

1. Log in to Strava
2. Go to **Settings** → **My Account** → **Download or Delete Your Account**
3. Click **Request Archive**
4. Wait for Strava to prepare your export (this may take several hours or days)
5. Download the ZIP file when it's ready

## ZIP File Structure Requirements

Your Strava export ZIP file must have the following structure:

```
your-strava-export.zip
├── activities.csv          # Required: CSV file with activity metadata
└── activities/            # Folder containing workout files
    ├── 1234567890.gpx     # GPX files (supported)
    ├── 1234567891.fit.gz  # Gzipped FIT files (supported)
    └── ...
```

### Requirements

- `activities.csv` must be in the root of the ZIP file
- Workout files (`.gpx` or `.fit.gz`) should be in the `activities/` folder
- The CSV `Filename` column should reference the file path (e.g., `activities/1234567890.gpx`)
- Only "Run" activities are imported (other activity types are skipped)

## Import Process

### Step 1: Prepare Your ZIP File

If your Strava export doesn't match the required structure:

1. Extract the ZIP file
2. Verify the structure matches the requirements above
3. Re-zip if needed to match the structure

### Step 2: Access Bulk Import

1. Log in to Tempo
2. Navigate to the Import page
3. Find the "Bulk Import Strava Export" section

### Step 3: Upload ZIP File

1. Click "Choose File" or drag and drop your ZIP file
2. Select your Strava export ZIP file
3. Files up to 500MB are supported

### Step 4: Wait for Processing

Bulk import processing may take several minutes depending on:
- Number of workouts in the export
- File sizes
- Server performance

You'll see progress updates during processing.

### Step 5: Review Import Summary

After processing completes, you'll see a summary showing:
- Number of workouts imported
- Number of workouts skipped (non-run activities or duplicates)
- Any errors encountered

## Duplicate Detection

Tempo automatically detects and skips duplicate workouts based on:
- Start time (`StartedAt`)
- Distance (`DistanceM`)
- Duration (`DurationS`)

If a workout with the same values already exists, it will be skipped.

## Supported Activity Types

Only "Run" activities are imported. Other activity types (cycling, swimming, etc.) are automatically skipped.

## File Size Limits

- Maximum ZIP file size: 500MB
- Individual workout files within the ZIP: No specific limit, but very large files may take longer to process

## Troubleshooting

### Import Fails

If bulk import fails:
- Verify the ZIP file structure matches requirements
- Check that `activities.csv` exists in the root
- Ensure workout files are in the `activities/` folder
- Verify file size is under 500MB
- Check API logs for specific error messages

### Missing Workouts

If some workouts aren't imported:
- Check the import summary for skipped workouts
- Verify the CSV `Filename` column matches actual file paths
- Ensure workout files exist in the ZIP
- Non-run activities are automatically skipped

### Processing Takes Too Long

If processing seems stuck:
- Large exports can take 10+ minutes
- Check API logs for progress
- Verify server has sufficient resources
- Consider splitting very large exports into smaller ZIP files

## Next Steps

- [View your imported workouts](viewing-workouts.md)
- [Configure settings](settings.md) for your preferences
- [Add media](media.md) to enhance your workouts


# Importing Workouts

Learn how to import individual workout files into Tempo.

## Supported File Formats

Tempo supports the following workout file formats:

- **GPX** (`.gpx`) - GPS Exchange Format, commonly used by Garmin, Strava, and other services
- **FIT** (`.fit`, `.fit.gz`) - Garmin's native format, supports compressed files
- **CSV** (`.csv`) - Strava export format with activity metadata

## How to Export from Different Devices

### Garmin Devices

1. Connect your Garmin device to your computer
2. Navigate to the device's storage
3. Find workout files in the `GARMIN/ACTIVITY/` directory
4. Copy `.fit` files directly

### Apple Watch

1. Open the Health app on your iPhone
2. Navigate to the workout you want to export
3. Use a third-party app or service to export as GPX
4. Alternatively, use the Health app's export feature if available

### Strava

1. Go to the activity page on Strava
2. Click the three-dot menu (⋮)
3. Select "Export GPX"
4. Download the GPX file

## Import Process

### Step 1: Access the Import Page

1. Log in to Tempo
2. Navigate to the Import page (usually accessible from the main navigation)

### Step 2: Upload Your File

You can import workouts in two ways:

**Drag and Drop:**
- Drag your workout file onto the import area
- Drop the file to start the upload

**File Selection:**
- Click the import area or "Choose File" button
- Select your workout file from the file picker

### Step 3: Wait for Processing

Tempo will:
- Detect the file format automatically
- Parse the workout data
- Calculate statistics (distance, pace, elevation, etc.)
- Fetch weather data based on location and time
- Generate splits and time-series data

### Step 4: View Your Workout

Once processing is complete, you'll be redirected to the workout details page where you can:
- View the route on an interactive map
- See detailed statistics
- Review splits
- Analyze time-series data (heart rate, pace, elevation)

## Import Multiple Files

You can import multiple files at once:

1. Select multiple files using Ctrl+Click (Windows/Linux) or Cmd+Click (macOS)
2. Or drag and drop multiple files onto the import area
3. All files will be processed sequentially

## What Gets Imported

For each workout, Tempo imports:

- **Route data** - GPS coordinates for map visualization
- **Statistics** - Distance, duration, pace, elevation gain/loss
- **Time-series data** - Heart rate, pace, elevation over time
- **Splits** - Distance-based splits (km or mile, based on your unit preference)
- **Weather data** - Automatic weather conditions for the workout
- **Metadata** - Activity name, date, time, device information

## Troubleshooting

### File Not Supported

If your file format isn't recognized:
- Ensure the file extension is correct (`.gpx`, `.fit`, `.fit.gz`, or `.csv`)
- Check that the file isn't corrupted
- Verify the file contains valid workout data

### Import Fails

If import fails:
- Check the file size (very large files may take longer)
- Ensure you have sufficient disk space
- Check the API logs for error messages
- Verify the file contains GPS data (required for route visualization)

### Missing Data

If some data is missing:
- Some devices may not record all metrics (e.g., heart rate)
- Weather data requires location and time information
- Elevation data depends on GPS accuracy

## Next Steps

- [Learn about bulk import](bulk-import.md) for importing many workouts at once
- [View your workouts](viewing-workouts.md) to explore imported data
- [Add media](media.md) to enhance your workouts


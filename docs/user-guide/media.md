# Adding Media to Workouts

Enhance your workouts by adding photos and videos.

## Overview

Tempo allows you to attach photos and videos to your workouts. This is useful for:
- Capturing scenery during your run
- Documenting special events or milestones
- Creating visual memories of your activities

## Supported Media Types

- **Images**: JPEG, PNG, GIF, WebP
- **Videos**: MP4, MOV, AVI (common video formats)

## File Size Limits

- Maximum file size: 50MB per file (configurable)
- Multiple files can be attached to a single workout
- Total storage depends on your available disk space

## Adding Media

### Step 1: Navigate to Workout

1. Go to the activities list
2. Click on the workout you want to add media to
3. This opens the workout details page

### Step 2: Upload Media

1. Find the "Media" section on the workout details page
2. Click "Add Media" or drag and drop files
3. Select one or more files from your computer
4. Wait for upload to complete

### Step 3: View Media

Once uploaded, media files appear in the workout details:
- Thumbnails for images
- Video players for video files
- Click to view full size or play videos

## Managing Media

### View Media

- Click on any media thumbnail to view full size
- Videos can be played directly in the browser
- Use navigation arrows to browse through multiple files

### Delete Media

To remove media from a workout:
1. Go to the workout details page
2. Find the media you want to delete
3. Click the delete button
4. Confirm deletion

**Note**: Deleting media permanently removes the file from storage.

## Media Storage

Media files are stored:
- On the filesystem in the `media/` directory
- Organized by workout GUID: `media/{workoutId}/filename.ext`
- Files are referenced in the database for quick access

## Best Practices

- **File Sizes**: Compress large images before uploading to save storage space
- **Naming**: Media files are automatically renamed to prevent conflicts
- **Backup**: Include the `media/` directory in your backups
- **Organization**: Add media immediately after a workout for better organization

## Troubleshooting

### Upload Fails

If media upload fails:
- Check file size (must be under 50MB)
- Verify file format is supported
- Ensure sufficient disk space is available
- Check API logs for specific errors

### Media Not Displaying

If media doesn't appear:
- Refresh the page
- Check that the file uploaded successfully
- Verify file format is supported
- Check browser console for errors

### Storage Issues

If you run out of storage:
- Delete unused media files
- Increase available disk space
- Configure a different media storage path (see [Configuration](../getting-started/configuration.md))

## Next Steps

- [View your workouts](viewing-workouts.md) with media
- [Configure settings](settings.md) for your preferences


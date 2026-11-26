# Adding Media to Workouts

Attach photos and videos to a Workout on its Workout overview. Strava bulk import also copies the photos listed in the export's CSV `Media` column.

## Supported Types and Limits

| Kind | Extensions |
|---|---|
| Images | `.jpg`, `.jpeg`, `.png`, `.gif`, `.webp` |
| Videos | `.mp4`, `.mov`, `.avi` |

- Max 50 MB per file (`MediaStorage:MaxFileSizeBytes`, default `52428800`). See [Configuration](../getting-started/configuration.md#storage-and-processing).
- Any number of files per Workout; several per upload.

## Uploading

1. Open the Workout overview.
2. In the left card, click **Add Media** and pick one or more files. Files of other types are skipped with an alert.
3. Remove any from the list with ×, then click **Upload N file(s)**.

Each file is validated separately. If some fail, the rest are saved. If none succeed, the request returns `400` with `No files were successfully uploaded` and a per-file `Failed to process file`.

## Viewing and Deleting

- Thumbnails appear under **Add Media**; videos show a video thumbnail.
- Click one to open the viewer. ← / → move between items (wrapping), Esc closes.
- Delete from the trash icon on a hovered thumbnail, the viewer's delete button, or Delete/Backspace in the viewer. Each asks for confirmation. The file is removed from disk.

Deleting a Workout deletes its media files too.

## Storage

- Files live under `MediaStorage:RootPath` (`./media` relative to the API working directory; `/app/media` in Compose, mounted from `./media`), in one folder per Workout: `{RootPath}/{workoutId}/{filename}`.
- Filenames are sanitized; a name that already exists in that folder gets a GUID suffix.
- Metadata (filename, MIME type, size, path) is in the `WorkoutMedia` table.
- Media is included in **Settings → Export** and restored by **Restore Tempo export**. For filesystem backups, include the media directory. See [Backup and Restore](../deployment/backup-restore.md).

**Implementation:** `frontend/components/WorkoutOverviewMedia.tsx`, `MediaUpload.tsx`, `WorkoutMediaGallery.tsx`, `MediaModal.tsx`; `api/Services/MediaService.cs`; `POST/GET /workouts/{id}/media`, `GET/DELETE /workouts/{id}/media/{mediaId}` in `api/Endpoints/WorkoutsEndpoints.cs`.

## Troubleshooting

| Symptom | Cause | Fix |
|---|---|---|
| `No files were successfully uploaded` | Every file failed. The API log says which: `Media file exceeds size limit: {FileName} ({Size} bytes)`, `Unsupported media file type: {FileName}`, or `Error uploading media file: {FileName}` (with the exception, e.g. directory not writable or disk full) | Compress the file or raise `MediaStorage__MaxFileSizeBytes`; convert HEIC to JPEG; check the media volume mount and `df -h`. |
| Thumbnail broken | File missing on disk (orphaned record) | Restore the media directory from backup, or delete the item. Deleting logs `Media file not found on filesystem (orphaned record)`. |

## Next Steps

- [View your workouts](viewing-workouts.md)
- [Configure settings](settings.md)

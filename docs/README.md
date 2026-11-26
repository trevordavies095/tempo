# Documentation Development

This directory contains the source files for the Tempo documentation site, built with [MkDocs Material](https://squidfunk.github.io/mkdocs-material/).

## Prerequisites

- Python 3 (CI uses 3.12)
- pip (Python package manager)

## Installation

Run these from the repository root (where `mkdocs.yml` and `requirements.txt` live):

```bash
python3 -m venv .venv && . .venv/bin/activate
pip install -r requirements.txt
```

## Development

```bash
mkdocs serve            # live reload at http://127.0.0.1:8000
mkdocs build --strict   # fail on broken links/anchors; run before opening a PR
```

The built site goes to `site/`. CI (`.github/workflows/docs.yml`) runs `mkdocs build` and does not use `--strict`, so broken anchors only show up as `INFO` lines unless you build strictly yourself.

## Project Structure

```
docs/
├── index.md                    # Homepage
├── getting-started/           # Getting started guides
├── user-guide/                # User documentation
│   └── screenshots/           # Copies for Markdown preview (same-folder paths)
├── developers/                # Developer documentation
├── deployment/                # Deployment guides
├── troubleshooting/           # Troubleshooting guides
└── assets/
    ├── screenshots/           # Canonical UI screenshots (README + docs home)
    └── diagrams/
```

## Configuration

Documentation configuration is in `mkdocs.yml` at the repository root. Maintainer drafts at the docs root (`PR-*.md`, `PRD-*.md`, and this README) stay in git but are excluded from the published site.

## Adding Content

### Creating New Pages

1. Create a new Markdown file in the appropriate directory
2. Add it to the navigation in `mkdocs.yml`
3. Use proper heading hierarchy (H1 for page title, H2 for sections, etc.)

### Adding Images

1. Place canonical screenshots in `docs/assets/screenshots/`
2. For pages under `docs/user-guide/`, also copy into `docs/user-guide/screenshots/` (editor Markdown previews do not reliably load `../` paths or symlinks). Keep both copies identical.
3. Reference them with same-folder relative paths (include the `./` prefix):
   - From `docs/index.md`: `![Alt text](./assets/screenshots/image.png)`
   - From `docs/user-guide/*.md`: `![Alt text](./screenshots/image.png)`
   - From the root `README.md`: `![Alt text](./docs/assets/screenshots/image.png)`
4. If your editor's preview blocks local images, allow them in its Markdown preview security setting (`.vscode/` is gitignored, so this is per-machine)


### Code Blocks

Use fenced code blocks with language tags:

````markdown
```bash
docker-compose up -d
```
````

### Admonitions

Use Material admonitions for notes, warnings, etc.:

```markdown
!!! note
    This is a note.

!!! warning
    This is a warning.
```

## Writing Conventions

These pages are read by developers and by coding agents. Write them so either can act on a page without opening five others:

- **Open with scope.** Start each page with 1–2 sentences saying what it covers and when to read it.
- **Point to the source of truth instead of copying it.** Domain terms: `CONTEXT.md`. HTTP contract: `docs/openapi.json`. Schema: `api/Data/TempoDbContext.cs`. Config keys: `getting-started/configuration.md`. Copy a list here only when it adds meaning the source lacks.
- **Name real paths and identifiers** (`api/Services/WorkoutIntake.cs`, `Workout.TimerTimeS`) so readers can jump straight to the code.
- **Use `CONTEXT.md` vocabulary** (Workout overview, Import job, Device lap), and avoid the terms it lists under _Avoid_.
- **Make commands copy-pasteable** and verify that they run.
- **Update docs in the same PR as the behavior change.** The local `mkdocs build --strict` catches broken links; it does not catch stale content.

## Deployment

Pushes to `main` that touch `docs/**`, `mkdocs.yml`, or `requirements.txt` trigger `.github/workflows/docs.yml`, which builds and publishes the site to GitHub Pages at <https://trevordavies095.github.io/tempo/>. Changes merged to `develop` do not appear on the site until the next release merge to `main`.

## Resources

- [MkDocs Documentation](https://www.mkdocs.org/)
- [Material for MkDocs](https://squidfunk.github.io/mkdocs-material/)
- [Markdown Guide](https://www.markdownguide.org/)


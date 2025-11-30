# Documentation Development

This directory contains the source files for the Tempo documentation site, built with [MkDocs Material](https://squidfunk.github.io/mkdocs-material/).

## Prerequisites

- Python 3.8 or higher
- pip (Python package manager)

## Installation

1. Install dependencies:

```bash
pip install -r ../requirements.txt
```

Or install directly:

```bash
pip install mkdocs-material pymdown-extensions
```

## Development

### Serve Locally

Start the development server with live reload:

```bash
mkdocs serve
```

The documentation will be available at `http://127.0.0.1:8000`.

### Build Documentation

Build the static site:

```bash
mkdocs build
```

The built site will be in the `site/` directory (excluded from git).

## Project Structure

```
docs/
├── index.md                    # Homepage
├── getting-started/           # Getting started guides
├── user-guide/                # User documentation
├── developers/                # Developer documentation
├── deployment/                # Deployment guides
├── troubleshooting/           # Troubleshooting guides
└── assets/                    # Images, diagrams, etc.
    ├── screenshots/
    └── diagrams/
```

## Configuration

Documentation configuration is in `mkdocs.yml` at the repository root.

## Adding Content

### Creating New Pages

1. Create a new Markdown file in the appropriate directory
2. Add it to the navigation in `mkdocs.yml`
3. Use proper heading hierarchy (H1 for page title, H2 for sections, etc.)

### Adding Images

1. Place images in `docs/assets/` (screenshots or diagrams subdirectories)
2. Reference them in Markdown: `![Alt text](assets/screenshots/image.png)`

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

## Deployment

The documentation can be deployed to:

- GitHub Pages
- Netlify
- Any static hosting service

See the [MkDocs deployment guide](https://www.mkdocs.org/user-guide/deploying-your-docs/) for details.

## Resources

- [MkDocs Documentation](https://www.mkdocs.org/)
- [Material for MkDocs](https://squidfunk.github.io/mkdocs-material/)
- [Markdown Guide](https://www.markdownguide.org/)


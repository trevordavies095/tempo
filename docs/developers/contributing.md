# Contributing Guide

How changes get into Tempo: branches, commits, pull requests, and what a PR must include. Read this before opening a pull request; set up first with [Development Setup](setup.md).

## Branches

- `develop` is the integration branch. Open pull requests against `develop`.
- `main` holds releases. Maintainers merge `develop` into `main` for each release; the docs site deploys from `main` (`.github/workflows/docs.yml`).

## Workflow

1. Fork the repository (maintainers branch directly) and clone it
2. Branch from `develop` with a type prefix:
   ```bash
   git checkout develop
   git pull
   git checkout -b feat/your-feature-name
   ```
   Prefixes in use: `feat/`, `fix/`, `refactor/`, `docs/`, `chore/`, `ci/` (e.g. `feat/garmin-device-names`, `fix/singleton-settings-row`)
3. Make the change, with tests (see [Pull Request Checklist](#pull-request-checklist))
4. Commit with a [Conventional Commits](https://www.conventionalcommits.org/) subject, matching the history:
   ```bash
   git commit -m "feat: name every Garmin device from the FIT SDK product tables"
   ```
   Types in use: `feat`, `fix`, `refactor`, `docs`, `chore`, `ci`. Add a body when the why is not obvious from the subject.
5. Push and open a pull request against `develop`:
   ```bash
   git push origin feat/your-feature-name
   ```

## Pull Request Checklist

CI (`.github/workflows/tests.yml`) must pass. Details and local commands: [Testing → What CI Runs](testing.md#what-ci-runs).

- [ ] API tests pass: `dotnet test Tempo.sln` from the repo root (Docker running)
- [ ] Line coverage stays at or above **45%**; new code comes with unit or integration tests
- [ ] `docs/openapi.json` regenerated if routes or route/Swagger metadata changed ([OpenAPI drift check](testing.md#openapi-drift-check))
- [ ] `npm run lint` and `npm test` pass in `frontend/`
- [ ] A migration is included if the EF model changed (`dotnet ef migrations add <Name>` from `api/`)
- [ ] Docs under `docs/` updated for user-visible or operator-visible changes; new domain terms added to `CONTEXT.md`

In the description, say what changed and why, how to test it, link related issues, and add screenshots for UI changes.

## Code Conventions

The task map in [Architecture → Where to change X](architecture.md#where-to-change-x) lists which files each kind of change touches.

### Backend (C#)

- Minimal API endpoints in `api/Endpoints/`: `private static async Task<IResult>` handlers with XML doc comments and route metadata (`WithName`, `WithSummary`, `Produces`)
- Business logic in `api/Services/`, registered in `api/Program.cs` and injected into handlers
- New import formats: decode adapter → `DecodedWorkout` → `WorkoutIntake.PersistAsync`; do not add a second persist pipeline in HTTP or bulk code
- Recompute derived Workout data through `WorkoutDerivations`, saying what changed (`Samples` or `HeartRateZones`)
- Use `CONTEXT.md` terms in names, comments, and docs (Workout intake, Import job, Workout overview, …)

### Frontend (TypeScript/React)

- Functional components with hooks; server state through TanStack Query; API calls in `frontend/lib/api.ts`
- Tailwind CSS with semantic tokens (`canvas`, `ink`, `volt`, …), not `blue-600` as the brand accent
- Prefer kit primitives in `frontend/components/ui/` (`PageShell`, `Card`, `Button`, `Dialog`, `EmptyState`, `Tabs`) over one-off page chrome
- **Icons**: Tabler Icons (`@tabler/icons-react`), e.g. `import { IconName } from '@tabler/icons-react'`. Do not invent custom SVG icons. Brand marks in `frontend/public/` (`tempo-mark-volt.png`, `tempo-mark-ink.png`) are the exception.

## Reporting Issues

- **Bugs**: use the [bug report form](https://github.com/trevordavies095/tempo/issues/new/choose). It asks for Tempo version, logging profile, steps to reproduce, and API logs; for import or query bugs, reproduce once with `TEMPO_LOGGING_PROFILE=debug`.
- **Feature requests**: open an issue with the use case, the motivation, and any implementation ideas or alternatives.

## Questions

- Check existing issues and pull requests, or open an issue
- Join the [Discord community](https://discord.gg/9Svd99npyj)

Be respectful and constructive in issues, reviews, and Discord.

## License

By contributing, you agree that your contributions will be licensed under the MIT License.

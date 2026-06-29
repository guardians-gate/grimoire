# dist artifacts

This directory stores persistent integration-test outputs.

- Tests emit artifacts to `dist/<project-name>/`.
- `dotnet clean` removes generated files under `dist/**` while preserving this `README.md`.
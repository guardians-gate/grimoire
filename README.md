# Grimoire

Grimoire compiles tabletop campaign content (Markdown, JSON, media, and settings) into:

- Website output
- PDF sourcebooks
- Foundry DB exports

It also ships a CLI, an MCP server mode for tooling integrations, D&D Beyond sync helpers, and a MAUI desktop UI.

Copyright © 2026 Guardian's Gate Games LLC.

## Important status note (UI call for maintainers)

`Grimoire.Ui` is functional but **known to be incomplete, buggy, and visually rough**.

If you are a UI engineer and want to help, please contribute to `src/Grimoire.Ui` and `tests/Grimoire.Ui.Tests`. The project needs active UI ownership for polish, UX consistency, and defect reduction.

## Repository layout

- `src/Grimoire.Core` - compilation engine, rendering pipeline, search/sync services, localization, templates.
- `src/Grimoire.Cli` - command-line interface and MCP server host.
- `src/Grimoire.Ui` - MAUI desktop UI (Linux GTK, Windows, Mac Catalyst targets).
- `tests/Grimoire.Core.Tests` - core unit/integration/regression coverage.
- `tests/Grimoire.Ui.Tests` - UI workflow and regression coverage.
- `projects/` - example/sourcebook projects.

## Prerequisites

1. .NET 10 SDK (repo pins `10.0.203` in `global.json` with feature roll-forward).
2. For UI builds:
   - **Linux**: GTK/WebKit dev packages.
   - **macOS**: Xcode + MAUI workloads.
   - **Windows**: WinUI-capable .NET/MAUI environment.

## Build and test

```bash
dotnet clean
dotnet build
dotnet test
```

Targeted suites:

```bash
dotnet test tests/Grimoire.Core.Tests/Grimoire.Core.Tests.csproj -f net10.0
dotnet test tests/Grimoire.Ui.Tests/Grimoire.Ui.Tests.csproj -f net10.0
```

## CLI quick start

Run via project:

```bash
dotnet run --project ./src/Grimoire.Cli/Grimoire.Cli.csproj -- <command> [options]
```

Root subcommands:

- `compile`
- `new`
- `mcp`
- `dnd-beyond` (aliases: `dndb`, `ddb`)
- `search`

Global option:

- `--verbose`, `-v`

### Compile

Compile from an input project/folder/archive to inferred output target:

```bash
# Website
dotnet run --project ./src/Grimoire.Cli/Grimoire.Cli.csproj -- compile -o ./out/site ./projects/projectName

# PDF
dotnet run --project ./src/Grimoire.Cli/Grimoire.Cli.csproj -- compile -o ./out/book.pdf ./projects/projectName

# Foundry DB
dotnet run --project ./src/Grimoire.Cli/Grimoire.Cli.csproj -- compile -o ./out/foundry.db ./projects/projectName
```

JSON-to-Markdown upgrade mode:

```bash
dotnet run --project ./src/Grimoire.Cli/Grimoire.Cli.csproj -- compile --upgrade -o ./projects/projectName/items
```

### Scaffold (`new`)

Create or reseed a project skeleton:

```bash
dotnet run --project ./src/Grimoire.Cli/Grimoire.Cli.csproj -- new ./my-sourcebook
dotnet run --project ./src/Grimoire.Cli/Grimoire.Cli.csproj -- new --force ./my-sourcebook
```

### Search

Catalog and advanced project search modes:

- `catalog` (default when no query/mode is provided)
- `cross-reference`
- `full-text`
- `keyword-usage`
- `property`

Examples:

```bash
dotnet run --project ./src/Grimoire.Cli/Grimoire.Cli.csproj -- search -i ./projects/projectName goblin
dotnet run --project ./src/Grimoire.Cli/Grimoire.Cli.csproj -- search -i ./projects/projectName -m property -P definition.name fire
dotnet run --project ./src/Grimoire.Cli/Grimoire.Cli.csproj -- search -i ./projects/projectName -p spells -p creatures -n 100
```

### D&D Beyond sync

Sync content using a Cobalt token:

```bash
# token via env
export DND_BEYOND_COBALT="<token>"
dotnet run --project ./src/Grimoire.Cli/Grimoire.Cli.csproj -- dnd-beyond -o ./projects/projectName

# explicit token + options
dotnet run --project ./src/Grimoire.Cli/Grimoire.Cli.csproj -- dnd-beyond \
  -k "<token>" \
  -o ./projects/projectName \
  --homebrew \
  --campaign 123456 \
  -I "Long*" -S "Fire*" -M "Gob?in"
```

Useful options:

- `--campaign`, `-C`
- `--character-sheet`, `-P`, `--player`
- `--cobalt`, `-k`
- `--creature`, `-M`, `--monster`
- `--homebrew`, `-H`
- `--item`, `-I`
- `--output`, `-o`
- `--patreon-key`, `-K`, `--patreon`
- `--self-proxy`, `-s`
- `--spell`, `-S`
- `--upgrade`, `-u`

### MCP mode

Run MCP server in current directory or specific project root:

```bash
dotnet run --project ./src/Grimoire.Cli/Grimoire.Cli.csproj -- mcp
dotnet run --project ./src/Grimoire.Cli/Grimoire.Cli.csproj -- mcp ./projects/projectName
```

## UI build/run notes

`src/Grimoire.Ui` targets:

- Linux GTK (`net10.0`)
- Windows (`net10.0-windows10.0.22000.0`)
- Mac Catalyst (`net10.0-maccatalyst`)

Examples:

```bash
# Linux GTK
dotnet build ./src/Grimoire.Ui/Grimoire.Ui.csproj -f net10.0

# Windows
dotnet build ./src/Grimoire.Ui/Grimoire.Ui.csproj -f net10.0-windows10.0.22000.0

# Mac Catalyst
dotnet build ./src/Grimoire.Ui/Grimoire.Ui.csproj -f net10.0-maccatalyst
```

Linux package prerequisites (Debian/Ubuntu):

```bash
sudo apt update
sudo apt install libgtk-4-dev libwebkitgtk-6.0-dev
```

## External attribution and licenses

See [`CONTRIB.md`](./CONTRIB.md) for consolidated third-party attribution and license details (including Humanizer and font assets).


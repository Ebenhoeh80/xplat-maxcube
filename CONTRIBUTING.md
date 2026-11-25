# Contributing

Thanks for your interest in **xplat-maxcube**! This project keeps the local
MAX! Cube experience alive on Windows and Linux without cloud dependencies.

## Ground rules

Please do **not** contribute:

- cloud, remote access, product account, proxy or app-update functionality,
- firmware writing to the Cube,
- Jetty, browser or HTTP server dependencies,
- automatic UDP broadcast or Cube writes on application start.

Every Cube write must remain tied to an explicit user action. A UDP broadcast
is only allowed from the user-triggered "Cube suchen" command. The Cube is the
source of truth; SQLite is a cache and audit log only.

## Development setup

1. Install the .NET 10 SDK.
2. Clone the repository and run `dotnet restore MaxCube.slnx`.
3. Build and test before opening a pull request:

   ```bash
   dotnet build MaxCube.slnx --nologo
   dotnet test MaxCube.slnx --nologo
   dotnet src/MaxCube.UI.Desktop/bin/Debug/net10.0/MaxCube.UI.Desktop.dll --selfcheck
   ```

4. On Windows use `.\start.cmd`, on Linux `bash start.sh` to launch the app.

CI runs on both `ubuntu-latest` and `windows-latest`; please make sure both
jobs pass. See [docs/DEVELOPMENT.md](docs/DEVELOPMENT.md) for conventions.

## Protocol changes

This project reverse-engineers a proprietary wire protocol. Be explicit about
how you know something:

| Marker | Meaning |
|---|---|
| `FAKT` | Directly evidenced in decompiled code, C# code, or real hardware traffic |
| `BEOBACHTUNG` | Observed in a test/run, not proven as protocol semantics |
| `INFERENZ` | Derived from evidence, not verified on hardware |
| `UNBEKANNT` | No evidence yet |

Always add a `file:line` reference. Real hardware captures belong in
[docs/HARDWARE-VERIFY.md](docs/HARDWARE-VERIFY.md). Never write to a real Cube
without a confirmed user action and a plan to restore the original values.

The reverse-engineering inputs (`decompiled/`, `extracted/`, `evidence/`) are
**not** part of this repository for licensing reasons. Only our own analysis
under `docs/` is public. If you need those inputs for local research, keep them
local and never commit them.

## Pull requests

- Keep changes focused and add or update tests for protocol changes.
- Update [CHANGELOG.md](CHANGELOG.md) under `Unreleased` with Added/Changed/Fixed.
- Describe how you verified the change (unit tests, selfcheck, hardware capture).
- Use English or German; both are welcome in issues and pull requests.

## Reporting bugs

Open an issue with the app version, Windows/Linux distribution, the exact steps
to reproduce, and the relevant log excerpt. Redact serial numbers and addresses
from your own environment if you prefer. Do not attach proprietary original
application files to issues.

## Releases

Releases are created from `vMAJOR.MINOR.PATCH` tags by
[`.github/workflows/release.yml`](.github/workflows/release.yml). Pre-release
tags such as `v0.5.0-rc.1` are marked automatically. Maintainers should add a
CHANGELOG section for the tag before pushing it.

## License

By contributing you agree that your contribution is licensed under the
[MIT License](LICENSE).

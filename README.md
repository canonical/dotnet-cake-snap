# Cake Snap

A `classic` confined snap for the [Cake](https://cakebuild.net/) .NET build automation tool.
The snap exposes the upstream `dotnet-cake` command as `dotnet-cake`.

## What is Cake?

Cake (C# Make) lets developers write build scripts in C# using a simple DSL. A typical
Cake user creates a `build.cake` file and runs `dotnet-cake` to execute tasks such as compiling,
testing, packaging, and deploying.

## Build

`snap/snapcraft.yaml` is **generated** from `snap/local/template.snapcraft.yaml` by the
`Makefile` (it is git-ignored). Build with `make`, which fetches the latest Cake release
from GitHub, generates the yaml, and packs the snap:

```bash
# Build with the latest Cake release (fetched from GitHub)
make

# Build a specific version
make VERSION=6.2.0

# Build an edge snapshot from a commit SHA (grade=devel, branch=develop)
make VERSION=abc1234 GRADE=devel BRANCH=develop
```

You can also generate the yaml only (without packing) for inspection:

```bash
make generate-snapcraft VERSION=6.2.0
```

### Prerequisites

```bash
sudo apt install make snapcraft curl jq
```

## Install locally

```bash
snap install --dangerous --classic ./dotnet-cake_*.snap
```

## Usage

```bash
# Check the version
dotnet-cake --version

# Show help
dotnet-cake --help

# Run a build.cake in the current directory
dotnet-cake

# Run a specific script
dotnet-cake my-script.cake
```

## Example build.cake

```csharp
Task("Default")
    .Does(() =>
    {
        Information("Hello from Cake snap!");
    });

RunTarget("Default");
```

Save the file as `build.cake` and run `dotnet-cake` in the same directory.

## Testing the snap

This repository includes an integration test fixture under `tests/`. It contains a small .NET 10
console app (`Program.cs`) and a comprehensive `build.cake` that exercises many Cake aliases and features
(globalization, file IO, globbing, compression, hashing, external processes, NuGet addins, retries,
parallel tasks, and full `dotnet` CLI restore/build/publish).

### Run the fixture against the snap

```bash
# Build the snap (fetches the latest Cake release and packs it)
make

# Install the resulting .snap locally
snap install --dangerous --classic ./dotnet-cake_*.snap

# Move into the fixture directory
cd tests

# Run the default target (build + create artifact)
dotnet-cake build.cake

# Run the full regression target
dotnet-cake --target=Full
```

Because the snap is `classic`, the script can use the host `dotnet` SDK and other system tools.
If you also have Cake installed globally or via another source, use `snap run dotnet-cake` to force
the snap binary:

```bash
snap run dotnet-cake --target=Full
```

### What the fixture validates

- Cake compiles and runs the script, including the NuGet addin `Cake.FileHelpers`.
- `dotnet restore`, `dotnet build`, `dotnet test`, and `dotnet publish` work against a real project.
- `Zip` / `Unzip` and SHA256 hashing work inside the snap environment.
- The diagnostic app (`Program.cs`) runs successfully and reports runtime/environment info.
- Exit codes propagate correctly (the fixture intentionally tests `--fail` returning `42`).

## Continuous Integration

Three GitHub Actions workflows build and publish the snap (amd64 + arm64):

| Workflow | Trigger | Source | Channel | Purpose |
|---|---|---|---|---|
| `build-stable-snap.yml` | weekly cron + manual | latest Cake release tag | stable | Track new upstream Cake releases |
| `build-edge-snap.yml` | daily cron + manual | `develop` branch upstream | edge | Track upstream development |
| `ci-snap.yml` | push/PR to `main` | latest Cake release tag | edge (on merge only) | Validate changes to this repo's packaging |

The scheduled workflows use `eng/snap_store_has_latest.py` to skip the build when the Snap Store
already has the latest version. Publishing is opt-in via the `publish` workflow input (except
`ci-snap.yml`, which publishes to edge automatically on every push to `main`).

### Required secrets

- `SNAPCRAFT_STORE_CREDENTIALS` — Snap Store credentials from `snapcraft export-login`.

## Upstream

Built from the upstream Git source at `https://github.com/cake-build/cake`. The version is
determined at build time by `make fetch-version` (latest release) or passed explicitly via
`VERSION=`.

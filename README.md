# Cake Snap

[![Get it from the Snap Store](https://snapcraft.io/en/dark/install.svg)](https://snapcraft.io/dotnet-cake)

This repository contains the snap packaging for [Cake](https://cakebuild.net/) (C# Make),
a cross-platform build automation system with a C# DSL. The snap exposes the upstream
`dotnet-cake` command.

## What is Cake?

Cake (C# Make) lets developers write build scripts in C# using a simple DSL. A typical
Cake user creates a `build.cake` file and runs `dotnet-cake` to execute tasks such as compiling,
testing, packaging, and deploying.

## Supported Architectures

- **amd64** (x86_64)
- **arm64** (aarch64)

## Repository Structure

- `snap/` - Snap packaging files
  - `local/template.snapcraft.yaml` - Template snapcraft YAML with placeholders for dynamic values
- `.github/workflows/` - CI/CD automation
- `scripts/` - Engineering scripts
  - `snap_store_has_latest.py` - Version checking script for Snap Store channels
- `tests/` - Integration test fixture
- `Makefile` - Build automation for snap packaging

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

- **make** - Build automation tool
- **snapcraft** - Snap packaging tool
- **curl** - For fetching latest version from GitHub
- **jq** - For parsing JSON responses

Install dependencies on Ubuntu:

```
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

For a real-world example, see the integration test fixture at [`tests/build.cake`](tests/build.cake).

## Upstream

Built from the upstream Git source at `https://github.com/cake-build/cake`. The version is
determined at build time by `make fetch-version` (latest release) or passed explicitly via
`VERSION=`.

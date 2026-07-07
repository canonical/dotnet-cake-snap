# Integration tests

This directory contains an integration test fixture for the `dotnet-cake` snap.
It includes a small .NET 10 console app (`Program.cs`) and a comprehensive
`build.cake` that exercises many Cake aliases and features (globalization, file
IO, globbing, compression, hashing, external processes, NuGet addins, retries,
parallel tasks, and full `dotnet` CLI restore/build/publish).

## Run the fixture against the snap

```bash
# Build the snap (fetches the latest Cake release and packs it)
make

# Install the resulting .snap locally
snap install --dangerous --classic ./dotnet-cake_*.snap

# Move into the fixture directory
cd tests

# Run the full regression suite (default target runs everything)
dotnet-cake build.cake
```

Because the snap is `classic`, the script can use the host `dotnet` SDK and
other system tools. If you also have Cake installed globally or via another
source, use `snap run dotnet-cake` to force the snap binary:

```bash
snap run dotnet-cake build.cake
```

## What the fixture validates

- Cake compiles and runs the script, including the NuGet addin `Cake.FileHelpers`.
- `dotnet restore`, `dotnet build`, `dotnet test`, and `dotnet publish` work against a real project.
- `Zip` / `Unzip` and SHA256 hashing work inside the snap environment.
- The diagnostic app (`Program.cs`) runs successfully and reports runtime/environment info.
- Exit codes propagate correctly (the fixture intentionally tests `--fail` returning `42`).

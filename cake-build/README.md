# Cake Build System for ably-dotnet SDK

This is a C# Cake build project for building, testing and packaging the ably-dotnet SDK.

## Why Cake Build?

Migrated from FAKE (F#) to Cake (C#) to:
- Make build system accessible to all C# developers
- Improve maintainability with better IDE support and debugging
- Leverage larger community and better documentation

## Prerequisites
- .NET SDK 6.0+ (for building)
- Cake.Tool (installed via `dotnet tool restore`)
- NuGet CLI (for package creation; Windows or Mono)

## Getting Started

Clone the project and download Cake tools by running the following command at root:
```bash
dotnet tool restore
```

Running `.\build.cmd` (Windows) or `./build.sh` (Unix/macOS) will start the build process. By default it builds the NetStandard project.

## Build Commands

### Build NetFramework
`src/Ably.PubSub.Core.NETFramework` targets .NET Framework 4.6.2.

**Windows:**
```cmd
.\build.cmd --target=Build.NetFramework
```

### Build NetStandard
`src/Ably.PubSub.Core` targets netstandard2.0, net6.0 and net7.0.

**Windows:**
```cmd
.\build.cmd --target=Build.NetStandard
```

**Unix/macOS:**
```bash
./build.sh --target=Build.NetStandard
```

## Test Commands

### Test NetFramework

**Run unit tests:**
```cmd
.\build.cmd --target=Test.NetFramework.Unit
.\build.cmd --target=Test.NetFramework.Unit.WithRetry  # Retry failed tests
```

**Run integration tests:**
```cmd
.\build.cmd --target=Test.NetFramework.Integration
.\build.cmd --target=Test.NetFramework.Integration.WithRetry  # Retry failed tests
```

### Test NetStandard

**Run unit tests:**
```bash
./build.sh --target=Test.NetStandard.Unit
./build.sh --target=Test.NetStandard.Unit.WithRetry  # Retry failed tests
```

**Run integration tests:**
```bash
./build.sh --target=Test.NetStandard.Integration
./build.sh --target=Test.NetStandard.Integration.WithRetry  # Retry failed tests
```

**Target specific framework:**

Additional `--framework` flag can be supplied to test for target framework `net6.0` or `net7.0`:
```bash
./build.sh --target=Test.NetStandard.Unit --framework=net6.0  # Run tests for .NET 6.0 runtime
./build.sh --target=Test.NetStandard.Unit --framework=net7.0  # Run tests for .NET 7.0 runtime
```

## Create NuGet Packages

`Ably.PubSub.Core`, `Ably.PubSub.Device` and `Ably.PubSub.Server` are packed and
released together at one version, and each door package pins the core at exactly
that version. The `Package` target enforces that; see
`cake-build/tasks/release.cake`.

> **Cake reserves `--version` for itself**, so release arguments have to go after
> a `--` separator: `./build.sh -- --target=Package --version=2.0.0`. `package.cmd`
> already does this for you.

### package.cmd

- Responsible for creating all three NuGet packages.
- Works only on Windows (or with Mono): the core and server packages carry a
  `lib/net46` asset built by an old-style MSBuild head, and `NuGetPack` needs
  `nuget.exe`.

```cmd
.\package.cmd 2.0.0
```

Above command creates `Ably.PubSub.Core.2.0.0.nupkg`,
`Ably.PubSub.Device.2.0.0.nupkg` and `Ably.PubSub.Server.2.0.0.nupkg` at root,
from the nuspec list in `cake-build/tasks/package.cake`
(`_Package_Create_NuGet`), which is packed core-first to match the publish order.
Add `--packageOutput=<dir>` to pack somewhere other than the repository root.

During release process, these packages are hosted on
[nuget.org/packages/Ably.PubSub.Server](https://www.nuget.org/packages/Ably.PubSub.Server),
[.Device](https://www.nuget.org/packages/Ably.PubSub.Device) and
[.Core](https://www.nuget.org/packages/Ably.PubSub.Core).

### package-unity.sh

Responsible for creating the Unity package.

```bash
./package-unity.sh 2.0.0
```

Above command creates `ably.pubsub.2.0.0.unitypackage` at root. The merged
plugin assembly it packages is produced separately by
`./unity-plugins-updater.sh 2.0.0`, which needs Mono (for ILRepack).

## Release Targets

These are the release guards. They are wired into `Package` - `_Release_Preflight`
before the build, `_Release_Verify_Files` between the build and the pack, and
`_Release_Verify_Packages` after it - so an ordinary `Package` run already
enforces all of them. They are also public targets so they can be run alone.

### Release.Preflight

```bash
./build.sh -- --target=Release.Preflight --version=2.0.0
```

Source-only, no build, no pack, no network. Asserts that:

- the `--version` input equals all three attributes in `src/CommonAssemblyInfo.cs`
  and equals `unity/Assets/Ably/version.txt`;
- `nuget/` holds *exactly* `ably.pubsub.core.nuspec`, `ably.pubsub.device.nuspec`
  and `ably.pubsub.server.nuspec`, with the matching `<id>`s, each taking its
  version from `$version$`;
- every dependency group of both door nuspecs pins `Ably.PubSub.Core` as
  `[$version$]` - the exact range, not a minimum;
- nothing depends on `ably.io` or the 1.x push satellites.

Because it demands the 2.0 nuspec set, it also refuses a 1.x checkout outright,
which is what makes `publish.yml` safe to keep on the default branch.

### Release.VerifyPackages

```bash
./build.sh -- --target=Release.VerifyPackages --version=2.0.0 --packageOutput=<dir>
```

Post-pack. Opens each produced `.nupkg` and asserts its id, its version, one
`lib/<tfm>` assembly for every target the nuspec declares, and - the one that
cannot be checked from source - that the packed door dependency is literally
`[<version>]`, proving the `$version$` token substituted inside the dependency's
version attribute.

The `release-dry-run` GitHub workflow runs the pre-flight, a full `Package` and
this target on every pull request. `publish.yml` runs the same three and then
pushes.

## Advanced Options

### Build with specific configuration
```bash
.\build.cmd --target=Build.NetStandard --configuration=Debug
```

### Build with custom constants
```bash
.\build.cmd --target=Build.NetStandard --define=MY_CONSTANT
```

### Verbose output
```bash
.\build.cmd --target=Build.NetStandard --verbosity=diagnostic
```

### List all available targets
```bash
.\build.cmd --description
```

**Note:** This command shows all tasks including internal tasks (starting with `_`). Internal tasks are implementation details and should not be called directly. Use the public targets listed in this README instead.

### Show task dependency tree
```bash
.\build.cmd --tree
```

### Dry run (test without execution)
```bash
.\build.cmd --target=Build.NetStandard --dryrun
```

## Resources

- [Cake Build Official Documentation](https://cakebuild.net/docs/)
- [Cake Build API Reference](https://cakebuild.net/api/)

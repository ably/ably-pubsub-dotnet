# Cake Build System for ably-pubsub-dotnet SDK

This is a C# Cake build project for building, testing and packaging the ably-pubsub-dotnet SDK.

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
that version: every nuspec takes `$version$` from `--version`, and every door
dependency group pins `[$version$]`. `PackagingSpecs` (in the unit tests) asserts
that contract from the nuspec files on every PR.

> **Cake reserves `--version` for itself**, so packaging arguments have to go after
> a `--` separator: `./build.sh -- --target=Package --version=2.0.0`. `package.cmd`
> already does this for you. `Package` fails if no `--version` is given.

Releases are packaged by the `Package ably` GitHub workflow
(`.github/workflows/package.yml`), dispatched by hand with a `version` input. It
runs `Package` and `Test.PackageConsumption.NoPack` on Windows and `UnityPackage`
on Ubuntu, and uploads the three `.nupkg` files and the `.unitypackage` as one
`output-package` artifact. Nothing is published: the release engineer pushes the
packages to nuget.org by hand, following the release process in `CONTRIBUTING.md`.

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
from the package set at the top of `cake-build/tasks/package.cake`
(`nugetPackageSet`), which is packed core-first to match the manual publish order.
`package.cmd` takes only the version. To pack somewhere other than the repository
root, run the target directly with `--packageOutput`, e.g.
`.\build.cmd -- --target=Package --version=2.0.0 --packageOutput=packages` (or
`./build.sh` with the same arguments on Unix with Mono); a relative path is
resolved against the repository root.

After the pack, `Package` runs `_Package_Verify_NuGet`, which opens each produced
`.nupkg` and asserts its version and that every dependency group of both door
packages pins `Ably.PubSub.Core` as exactly `[<version>]`. That is the one part of
the contract the source nuspecs cannot prove: it depends on the packer substituting
`$version$` inside the dependency's version attribute. To check packages that are
already packed: `./build.sh -- --target=Package.Verify --version=2.0.0 --packageOutput=<dir>`.

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

## Package-Consumption Test

### Test.PackageConsumption

```bash
./build.sh -- --target=Test.PackageConsumption --version=2.0.0 --packageOutput=<dir>
```

Installs the packed packages into small apps the way a customer does, so problems
that exist only in the shipped `.nupkg` files fail here rather than in a customer's
app. The unit tests can't catch these, because they use project references, which
skip NuGet's per-framework asset selection and the Package-only signing. For example,
a .NET Framework app gets the Device `netstandard2.0` asset paired with the Core
`net46` asset.

The target runs `Package`, then:

1. copies the apps in `tests/PackageConsumption` (`DeviceApp`, `ServerApp`) to
   `test-results/package-consumption`;
2. restores them through a generated `nuget.config` that takes `Ably.PubSub.*` only
   from the packed `.nupkg` files (package source mapping) and everything else from
   nuget.org, into a private global packages folder;
3. builds each app for every target framework in the matrix (`net462`, `net48`,
   `net6.0`, `net7.0`);
4. asserts from `obj/project.assets.json` which `lib/<tfm>` asset of each Ably
   package NuGet selected, and that the packages came from the local folder;
5. runs each app. The .NET Framework apps run on the .NET Framework CLR on Windows
   and on Mono elsewhere. Each app creates clients through its door and calls into
   the core: a channel lookup, a REST request and a realtime connection attempt.
   It also checks the side flag in the `Ably-Agent` header.

The checks run offline: no credentials, REST requests go to a stub handler and the
realtime connection targets a closed localhost port.

The expected asset pair for each app and framework is set in one place, the matrix
at the top of `cake-build/tasks/package-consumption.cake`. If the package layout
changes, update that matrix. A row naming a `lib/<tfm>` that the nuspec doesn't
declare fails the run.

`Test.PackageConsumption.NoPack` runs the same steps against packages that are
already in `--packageOutput`, without packing. The `package.yml` workflow runs it
on Windows against the packages it has just packed, the ones the release engineer
downloads.

The full `Test.PackageConsumption` runs on every PR in two workflows: the Windows
(NetFramework) workflow, where the .NET Framework apps run on the .NET Framework
CLR, and the macOS (Mono) workflow, where they run on Mono.

On macOS/Linux, `Package` needs Mono: it builds the `net46` heads with Mono MSBuild
and the other target frameworks with the .NET SDK.

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

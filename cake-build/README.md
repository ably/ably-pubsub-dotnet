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
- NuGet CLI (for package creation)

## Getting Started

Clone the project and download Cake tools by running the following command at root:
```bash
dotnet tool restore
```

Running `.\build.cmd` (Windows) or `./build.sh` (Unix/macOS) will start the build process. By default it builds the NetStandard project.

## Build Commands

### Build NetFramework
We have a dedicated NetFramework project targeting .NET Framework 4.6+.

**Windows:**
```cmd
.\build.cmd --target=Build.NetFramework
```

### Build NetStandard
NetStandard currently supports explicit targets for netstandard2.0, net6.0 and net7.0.

**Windows:**
```cmd
.\build.cmd --target=Build.NetStandard
```

**Unix/macOS:**
```bash
./build.sh --target=Build.NetStandard
```

### Build Xamarin
We have a Xamarin solution targeting Android and iOS.

**Unix/macOS:**
```bash
./build.sh --target=Build.Xamarin
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

Currently, we have two scripts to generate NuGet packages:

### 1. package.cmd

- Responsible for creating core `ably.io` NuGet package.
- Works only on Windows due to a dependency on the .NET Framework.

```cmd
.\package.cmd 1.2.3
```

Above command creates `ably.io.1.2.3.nupkg` package at root.

During release process, this package is hosted on [nuget.org/packages/ably.io](https://www.nuget.org/packages/ably.io).

### 2. package-push.sh / package-push.cmd

Responsible for creating push packages for Android and iOS.

Please take a look at [Push Notification Documentation](../PushNotifications.md) for usage.

**Unix/macOS:**
```bash
./package-push.sh 1.2.3
```

**Windows:**
```cmd
.\package-push.cmd 1.2.3
```

Above command creates `ably.io.push.android.1.2.3.nupkg` and `ably.io.push.ios.1.2.3.nupkg` packages at root.

During release process, these packages are hosted on:
- [nuget.org/packages/ably.io.push.android](https://www.nuget.org/packages/ably.io.push.android)
- [nuget.org/packages/ably.io.push.ios](https://www.nuget.org/packages/ably.io.push.ios)

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

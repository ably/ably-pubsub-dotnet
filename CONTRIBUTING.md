# Contributing

## Development Flow

1. Fork it
2. Create your feature branch (`git checkout -b my-new-feature`)
3. Commit your changes (`git commit -am 'Add some feature'`)
4. Ensure you have added suitable tests and the test suite is passing
5. Push to the branch (`git push origin my-new-feature`)
6. Create a new Pull Request

## Building and Packaging

- The build scripts are written using [Cake Build](https://cakebuild.net/docs/), a C# build automation system.
- For more information on building, testing and packaging, take a look at [Cake build scripts](./cake-build/README.md).

## Working from source

If you want to incorporate the SDK into your project from source (perhaps to use a specific development branch) the simplest way to do so is to add references to the relevant projects from this repository. The following steps are specific to Visual Studio 2019, but the principle should transfer to other IDEs.

1. Clone this repository to your local system (`git clone --recurse-submodules https://github.com/ably/ably-pubsub-dotnet.git`)
2. Open the solution you want to reference the SDK from
3. In Solution Explorer right click the root node (it will be labelled Solution 'YourSolutionName')
4. Select Add > Existing Project from the context menu
5. Browse to the ably-pubsub-dotnet repository and add ably-pubsub-dotnet\src\Ably.PubSub.Shared\Ably.PubSub.Shared.shproj
6. Add the projects for the side your code runs on. The client constructors are internal, so clients are created through the door's `PubSubServer` or `PubSubDevice` factories.
    - A server app on .NET Framework: add ably-pubsub-dotnet\src\Ably.PubSub.Core.NETFramework\Ably.PubSub.Core.NETFramework.csproj and ably-pubsub-dotnet\src\Ably.PubSub.Server.NETFramework\Ably.PubSub.Server.NETFramework.csproj.
    - Any other server app: add ably-pubsub-dotnet\src\Ably.PubSub.Core\Ably.PubSub.Core.csproj and ably-pubsub-dotnet\src\Ably.PubSub.Server\Ably.PubSub.Server.csproj.
    - A device app, on any target framework including .NET Framework: add ably-pubsub-dotnet\src\Ably.PubSub.Core\Ably.PubSub.Core.csproj and ably-pubsub-dotnet\src\Ably.PubSub.Device\Ably.PubSub.Device.csproj.
7. In any project that you want to use the SDK in, you need to add a project reference to the door project, to do so:
    1. Find your project in Solution Explorer and expand the tree so that the Dependencies node is visible
    2. Right click Dependencies and select Add Reference
    3. In the dialogue that opens you should see a list of the projects in your solution. Check the box next to the door project you added (Ably.PubSub.Server, Ably.PubSub.Server.NETFramework or Ably.PubSub.Device) and click OK.

## Spec

The dotnet library follows the Ably [Features spec](https://docs.ably.com/client-lib-development-guide/features/). To ensure it is easier to look up whether a spec item has been implemented or not; we add a Trait attribute to tests that implement parts of the spec. The convention is to add `[Trait("spec", "spec tag")]` to unit tests.

To get a list of all spec items that appear in the tests you can run a script located in the tools directory.
You need a .NET SDK, 6.0 or later, installed. It works on Mac, Linux and Windows. Run `dotnet fsi tools/list-test-categories.fsx`. It will produce a `results.csv` file which will include all spec items, which file it was found and on what line.

## Development, Debugging and Testing

The Ably .NET SDK is written primarily in C#. In terms of platform support the Ably .NET SDK is broader than most. We explicitly support Windows, Linux and macOS as well as the Unity runtime, eventually, everywhere that Unity runs.

The Ably .NET SDK is available in two configurations: .NET Framework for legacy Windows clients and .NET (Core) for contemporary Windows clients, Linux and macOS. The bulk of the code for both the ‘Framework and ‘Core configurations is packaged in a shared assembly built against the .NET Standard 2.0 specification, while this allows for sharing it does limit the version of C# we are able to use to within the shared library to C# 8.

### Development & Debugging

It is strongly recommended that you have access to a fully updated Windows 10 or Windows 11 development environment running (at the time of writing) Visual Studio 2022. While 3rd party development tools like JetBrains Rider run on Windows, Linux and macOS, Visual Studio 2022 provides the very best development and debugging environment for both .NET Framework and .NET (Core) configurations. Given the current maturity of Windows on ARM and its associated tooling it is advisable to either run these on actual WinTel hardware or on an Intel based MacBook Pro using Parallels Desktop.

When debugging any kind of Ably Protocol error the following debugging techniques have been shown to be effective...

#### Debugging Tip: Isolate the failure in a console application

The very first thing is to isolate the failure, reproduce with it the minimum line count and extraneous runtime machinery. Some of the cost of this can be passed back to the customer, be they an external client or a Ably Labs member, getting them to write their own isolated reproduction of the failure can sometimes help shake out API usage misunderstandings from API defects. Of course if the misunderstanding is rooted in a documentation deficiency then a new JIRA item can be created to capture this. The other advantage to having  the customer try and isolate the failure is that it actively demonstrates that the failure is being looked at. Once we have a reproducible failure we have the option of rescheduling the actual fix.

#### Debugging Tip: Enable diagnostic logging

_More detail to follow around this._

#### Debugging Tip: Enable break on exception throw

To track down sporadic communication problems, enable ‘break on throw’ in your debugger. The SDK catches many exceptions internally (logging them or retrying the operation), so they don't reach the caller; breaking when they are thrown shows where they start. If you see a `NullReferenceException`, trace it back to its source and the conditions that led to it.

#### Debugging Tip: Debug and Release execution traces can differ

_More detail to follow around this._

### Testing

Testing takes place primarily via GitHub Actions based infrastructure, here Windows, Linux and macOS assemblies are built, and integration tests are run. In addition to this support is being added so that the GitHub repository can orchestrate a remote Unity build via [Unity’s Cloud Build service](https://unity.com/products/cloud-build). There are various reasons why, in addition to GitHub’s infrastructure (for free, because this is an open, public repository), the paid-for Unity Cloud Build service is used:

1. **Physical license server**: Currently Unity requires a license server to be run on physical hardware. The license server is needed to securely allocate a temporary license to a GitHub Worker. We do not wish to provision our own dedicated physical or virtual license service instances, thus pointing us in the direction of Unity Cloud Build.

2. **Need to build for multiple platforms**: A huge part of the Unity value proposition is that Unity runs on just about every commercially significant gaming platform. It would not be financially or practically possible for Ably to replicate even a modest subset of this.

3. **Existing concerns with the stability of the GitHub infrastructure**: Currently our GitHub workflows test Windows, Linux and macOS. The root cause of this is bound up in the fact that the vast majority of our tests are integration tests that run against the remotely hosted Ably sandbox. Network uncertainty, combined with wildly varying runner performance leads to frequent timeouts and associated protocol failures. Until or if we ever manage to reign these in it would be foolish to try and add additional platforms to the existing GitHub infrastructure.

At the present time it is possible for tests to transition from green to red with no code changes on the part of the SDK team. There are two reasons for this:

1. **Unexpected changes to the sandbox**: These tend to occur when the sandbox either evolves ahead of the Ably API specification or improves its conformance to an existing specification point, which a client-side test then detects as a difference.

2. **Uncontrolled dependency evolution**: At the present time the majority of the 3rd party Ably .NET dependencies have open dependency requirements which is to say we specify a minimum but not a maximum or even discrete list of versions against which we certify and support conformance. For example, if the Newtonsoft Json .NET library is updated it will be used by our next build and may contribute to test failures.

When one or more Ably .NET test failures is observed it is worth checking against another Ably client SDK with similar capabilities to see if it is also suffering from similar failures. This is aided somewhat by the fact that tests are decorated (the specifics vary depending on the SDK) with metadata that points to the Ably Specification point the test was originally intended to verify conformance for. So, for example if it is observed that the Channel Presence tests appear to be failing for .NET then check to see if the Ably Go SDK is showing failures in the same area. It is not always enough to just check to see if the Ably Go SDK happens to be green as this just means it was green at the time the last pull request was created, updated or merged.

As touched upon above, the recommended hardware for replicating cross platform test failures is at least an Intel based MacBook Pro. Ideally the developer should have access to both Intel and Apple Silicon (ARM64) hardware. ARM based CLR implementations are not a 1:1 substitute for older, more mature Intel based implementations at least at the current time. You need both and will observe behavioral differences in test execution when comparing one platform against the other.

As of Windows 11, mainstream Windows on ARM is increasingly A Thing and can not be ignored, it’s not just Apple and Linux who are embracing non x86 native instruction sets.

Experience has shown you are better off running local VMs for guest OS images as their cloud-based alternatives suffer from lack of configuration and performance variability.

Note: it was recently decided to remove explicit mention of Windows 7 as a supported platform for the Ably .NET SDK. It is probably worth considering when we can also look to exclude Windows 8 and Windows 8.1 setting the minimum supported version to Windows 10.

Integration tests run against the `nonprod:sandbox` endpoint by default. To run them against a specific endpoint, specify it in the environment:

    ABLY_ENDPOINT=nonprod:staging ./build.sh --target=Test.NetStandard.Integration.WithRetry --framework=net6.0

## Release process

>Important Note 
>- If local dotnet environment + project setup is not available, please use [github codespaces](https://github.com/codespaces) instead.
>- We have created `.devcontainer` folder at root specifying necessary dependencies for environment setup needed for release process.
>- Visit the [ably-pubsub-dotnet](https://github.com/ably/ably-pubsub-dotnet) repo, click on `Code` button at the right corner and create codespace for current branch from `Codespaces` tab. It will take some time to create the environment. Once codespace is created for the project, you can proceed with release steps mentioned below.

This library uses [semantic versioning](http://semver.org/).

**`Ably.PubSub.Core`, `Ably.PubSub.Device` and `Ably.PubSub.Server` are released together, always, at one
version.** Each door package declares an exact dependency on `[<that version>]` of the core, so a door
released without its core, or at a different version from it, is unresolvable for consumers. There is
therefore no such thing as releasing one of the three: one version number is bumped in one commit, one run of
the [Package ably](https://github.com/ably/ably-pubsub-dotnet/actions/workflows/package.yml) workflow packs all
three, and you push them to nuget.org by hand, core first.

What checks this before you publish:

- `PackagingSpecs`, in the unit tests on every pull request, asserts the nuspec contract: exactly these three
  packages, each taking its version from the pack input, every door dependency group pinning the core as
  `[$version$]`, no dependency on the 1.x `ably.io` packages, and the two version files agreeing.
- The package-install test (`Test.PackageConsumption`) packs the three packages and installs them into small
  .NET Framework and .NET apps the way a customer would, on every pull request: on the .NET Framework CLR in
  the Windows (NetFramework) workflow and on Mono in the macOS (Mono) workflow. `package.yml` runs it again on
  the exact packages it uploads.
- The manual checks in step 9 below, on the packages you are about to push.

### Prerequisites (once)

- A nuget.org account that is a member of the `ably-realtime` organization, which owns the packages. Before
  the first 2.0 release, request the [ID prefix reservation](https://learn.microsoft.com/en-us/nuget/nuget-org/id-prefix-reservation)
  of `Ably.PubSub.` for that organization.
- A nuget.org API key for the release: owner `ably-realtime`, scope **Push new packages and package
  versions**, glob pattern `Ably.PubSub.*`, and a short expiry. Keep it in the team password manager, never in
  the repository or its secrets.
- The [GitHub CLI](https://cli.github.com/) (`gh`), signed in, and a .NET 6 or later SDK.
- Windows, or Mono on macOS/Linux, to run the package-install test locally (step 9).

### Steps

The shell examples use `VERSION=2.0.1`; set it to the version being released.

1. Create a branch for the release, named like `release/2.0.1`. Until the 2.0 integration branch merges to `main`, the release branch is `integration/v2`; after that it is `main`. The 2.0 docs must be on `main` before the packages are published, because the package README links to `https://github.com/ably/ably-pubsub-dotnet/blob/main/...`.
2. Bump the version and commit:
    - `src/CommonAssemblyInfo.cs`: `AssemblyVersion` and `AssemblyFileVersion` take the numeric part only (`2.0.1`), and `AssemblyInformationalVersion` takes the full version, including any prerelease label (`2.0.1-rc.1`).
    - `unity/Assets/Ably/version.txt`.
    - The `PackageReference Version="..."` examples in `README.md`.
    - If the year has changed, the `<copyright>` year in the three nuspecs under `nuget/`.

    Don't edit the nuspec versions or the door pins: they come from `$version$`, the version the packages are packed at.
3. Regenerate the Unity plugin **after** the bump: run `./unity-plugins-updater.sh 2.0.1` (needs Mono) or `.\unity-plugins-updater.cmd 2.0.1` at the root. It builds from the committed `src/CommonAssemblyInfo.cs`, and also rewrites `unity/Assets/Ably/version.txt`. Commit the regenerated `unity/Assets/Ably/Plugins/Ably.PubSub.Device.dll`. Check that its product version is the new version, e.g. in PowerShell: `(Get-Item unity/Assets/Ably/Plugins/Ably.PubSub.Device.dll).VersionInfo.ProductVersion`.
4. Run [`github_changelog_generator`](https://github.com/github-changelog-generator/github-changelog-generator) to automate the update of the [CHANGELOG](./CHANGELOG.md). This may require some manual intervention, both in terms of how the command is run and how the change log file is modified. Your mileage may vary:
    - The command you will need to run will look something like this: `github_changelog_generator -u ably -p ably-pubsub-dotnet --since-tag 2.0.0 --output delta.md --token $GITHUB_TOKEN_WITH_REPO_ACCESS`. Generate a token in [GitHub personal access token settings](https://github.com/settings/tokens/new?description=GitHub%20Changelog%20Generator%20token).
    - The `## [2.0.0]` section of the CHANGELOG is hand-written. If the generator produces its own 2.0.0 entry, merge that generated entry into the existing hand-written one rather than letting the two duplicate.
    - Using the command above, `--output delta.md` writes changes made after `--since-tag` to a new file.
    - The contents of that new file (`delta.md`) then need to be manually inserted at the top of the `CHANGELOG.md`, changing the "Unreleased" heading and linking with the current version numbers.
    - Also ensure that the "Full Changelog" link points to the new version tag instead of the `HEAD`.

    Commit this change: `git add CHANGELOG.md && git commit -m "Update change log."`.
5. Check the build and the packaging contract locally: `./build.sh --target=Build.NetStandard`, then `./build.sh --target=Test.NetStandard.Unit.WithRetry --framework=net6.0`. `PackagingSpecs` fails if the two version files disagree or a door pin has been loosened.
6. Push the branch and create a release PR (ensure you include an SDK Team Engineering Lead and the SDK Team Product Manager as reviewers) and gain approvals for it, then merge it to the release branch. The PR's checks include the package-install test on the .NET Framework CLR and on Mono.
7. **Package.** Dispatch the [Package ably](https://github.com/ably/ably-pubsub-dotnet/actions/workflows/package.yml) workflow on the release branch, with the new version (or use `Run workflow` in the Actions tab):

    ```shell
    VERSION=2.0.1
    gh workflow run package.yml --ref main -f version="$VERSION"
    # Give the run a few seconds to appear, then:
    RUN_ID=$(gh run list --workflow package.yml --branch main --limit 1 --json databaseId --jq '.[0].databaseId')
    gh run watch "$RUN_ID" --exit-status
    ```

    Use `--ref integration/v2` and `--branch integration/v2` while that is the release branch. Every job must succeed. The workflow publishes nothing.
8. **Download** the merged artifact:

    ```shell
    gh run download "$RUN_ID" -n output-package -D "release-$VERSION"
    ls "release-$VERSION"
    shasum -a 256 "release-$VERSION"/*
    ```

    It holds exactly `Ably.PubSub.Core.$VERSION.nupkg`, `Ably.PubSub.Device.$VERSION.nupkg`, `Ably.PubSub.Server.$VERSION.nupkg` and `ably.pubsub.$VERSION.unitypackage`. The SHA-256 values must match the ones the run printed in its "Merge artifacts into single directory" step. Run the commands in this and the next steps from the repository root, on the release commit. Don't commit the download directory; delete it after the release.
9. **Verify the packages before pushing anything:**
    1. **Version:** every file name carries the version, and it equals `AssemblyInformationalVersion` in `src/CommonAssemblyInfo.cs` on the release commit.
    2. **Exact core pin:** for each door package,

        ```shell
        unzip -p "release-$VERSION/Ably.PubSub.Device.$VERSION.nupkg" Ably.PubSub.Device.nuspec | grep -E '<version>|<group|<dependency'
        unzip -p "release-$VERSION/Ably.PubSub.Server.$VERSION.nupkg" Ably.PubSub.Server.nuspec | grep -E '<version>|<group|<dependency'
        ```

        Every `<group>` must contain `<dependency id="Ably.PubSub.Core" version="[2.0.1]" />`, **with the square brackets**: without them the pin is a minimum. `<version>` must be the release version, and nothing may depend on `ably.io`.
    3. **Contents:** `unzip -l` each package. Each has `README.md` and `icon.png` at the root, and these `lib/<tfm>` folders, as declared in the nuspecs under `nuget/`:
        - `Ably.PubSub.Core`: `lib/net46`, `lib/netstandard2.0`, `lib/net6.0` and `lib/net7.0`, each with `Ably.PubSub.Core.dll` and `IO.Ably.DeltaCodec.dll`;
        - `Ably.PubSub.Device`: `lib/netstandard2.0`, `lib/net6.0` and `lib/net7.0`, each with `Ably.PubSub.Device.dll`;
        - `Ably.PubSub.Server`: `lib/net46`, `lib/netstandard2.0`, `lib/net6.0` and `lib/net7.0`, each with `Ably.PubSub.Server.dll`.
    4. **Strong name:** every `Ably.PubSub.*.dll` in the three packages has the public key token `70c9e5c3d2c68b16`. The core grants the doors access to its internals by that key, so a mismatch breaks them at runtime. In PowerShell 7 (`pwsh`):

        ```powershell
        $v = '2.0.1'
        Remove-Item -Recurse -Force "release-$v/unpacked" -ErrorAction SilentlyContinue
        Get-ChildItem "release-$v/*.nupkg" | ForEach-Object {
            [IO.Compression.ZipFile]::ExtractToDirectory($_.FullName, (Join-Path "release-$v/unpacked" $_.BaseName))
        }
        Get-ChildItem "release-$v/unpacked" -Recurse -Filter 'Ably.PubSub.*.dll' | ForEach-Object {
            $token = [Reflection.AssemblyName]::GetAssemblyName($_.FullName).GetPublicKeyToken()
            '{0}  {1}' -f (($token | ForEach-Object { $_.ToString('x2') }) -join ''), $_.FullName.Substring($PWD.Path.Length + 1)
        }
        ```

    5. **Package-install test on the downloaded packages:** `package.yml` already ran it on Windows. Run it again on your machine, against the files you are about to push (on macOS/Linux this needs Mono):

        ```shell
        ./build.sh -- --target=Test.PackageConsumption.NoPack --version="$VERSION" --packageOutput="release-$VERSION"
        ```

        On Windows: `.\build.cmd -- --target=Test.PackageConsumption.NoPack --version=2.0.1 --packageOutput=release-2.0.1`. A relative `--packageOutput` is resolved against the repository root. All eight cases must pass.
    6. **README:** the `README.md` in each package is the repository README at the release commit, e.g. `unzip -p "release-$VERSION/Ably.PubSub.Core.$VERSION.nupkg" README.md | diff --strip-trailing-cr - README.md` (the packages are packed on Windows, so their README has CRLF line endings), and its absolute links to `https://github.com/ably/ably-pubsub-dotnet/blob/main/...` resolve.
    7. **Unity:** the `.unitypackage` carries the version in its name, and the plugin it bundles (the committed `unity/Assets/Ably/Plugins/Ably.PubSub.Device.dll`) has that product version (step 3).
10. **Push the core alone first.** One file at a time, never a glob such as `Ably.PubSub.*.nupkg`, which would push the doors without waiting for the core:

    ```shell
    read -rs NUGET_API_KEY  # paste the key; it is not echoed
    dotnet nuget push "release-$VERSION/Ably.PubSub.Core.$VERSION.nupkg" --api-key "$NUGET_API_KEY" --source https://api.nuget.org/v3/index.json
    ```

11. **Wait until the core is indexed** on nuget.org. A door pinned to a core version that isn't indexed yet can't be restored. This can take several minutes:

    ```shell
    until curl -fsS https://api.nuget.org/v3-flatcontainer/ably.pubsub.core/index.json | grep -q "\"$VERSION\""; do sleep 30; done
    ```

    Also check that the [package page](https://www.nuget.org/packages/Ably.PubSub.Core) shows the version as validated.
12. **Push the device package, then the server package**, one at a time, with the same command:

    ```shell
    dotnet nuget push "release-$VERSION/Ably.PubSub.Device.$VERSION.nupkg" --api-key "$NUGET_API_KEY" --source https://api.nuget.org/v3/index.json
    dotnet nuget push "release-$VERSION/Ably.PubSub.Server.$VERSION.nupkg" --api-key "$NUGET_API_KEY" --source https://api.nuget.org/v3/index.json
    ```

    If a push fails partway, carry on from the first package that isn't on nuget.org yet. Pushing a version that is already there is rejected; it never overwrites.
13. **Confirm** that all three packages are listed at the new version on nuget.org, and that each door's dependencies show `Ably.PubSub.Core (= 2.0.1)`.
14. **Tag the packaged commit and create the GitHub release** with the `.unitypackage` attached:

    ```shell
    git fetch origin
    git tag "$VERSION" "$(gh run view "$RUN_ID" --json headSha --jq .headSha)"
    git push origin "$VERSION"
    gh release create "$VERSION" "release-$VERSION/ably.pubsub.$VERSION.unitypackage" --title "$VERSION" --notes "See the [CHANGELOG](https://github.com/ably/ably-pubsub-dotnet/blob/main/CHANGELOG.md) for the changes in $VERSION."
    ```

    Add `--prerelease` for a version with a prerelease label, e.g. `2.0.1-rc.1`. The tag is the bare version; the CHANGELOG links to `tree/<version>`.
15. **Unity:** check that the GitHub release has the `.unitypackage`, and, for a stable release, that [the latest release](https://github.com/ably/ably-pubsub-dotnet/releases/latest), which the Unity install instructions link to, is this one.
16. **After the release:**
    - Check the nuget.org pages for [`Ably.PubSub.Core`](https://www.nuget.org/packages/Ably.PubSub.Core), [`Ably.PubSub.Device`](https://www.nuget.org/packages/Ably.PubSub.Device) and [`Ably.PubSub.Server`](https://www.nuget.org/packages/Ably.PubSub.Server): the README renders (header image, badges, tables, callouts) and its links resolve, and the icon, license and project URL are right.
    - Create the entry on the [Ably Changelog](https://changelog.ably.com/) (via [headwayapp](https://headwayapp.co/)).
    - Revoke the API key, or let it expire.
    - For 2.0.0, a few days after GA: carry out the `ably.io` deprecation plan in https://github.com/ably/ably-pubsub-dotnet/issues/1356.

**If a release has to be abandoned:** NuGet can't delete a published version, and the version number can never be reused. Unlist the packages, the doors first and then the core, and release a new version:

```shell
dotnet nuget delete Ably.PubSub.Server "$VERSION" --source https://api.nuget.org/v3/index.json --api-key "$NUGET_API_KEY" --non-interactive
dotnet nuget delete Ably.PubSub.Device "$VERSION" --source https://api.nuget.org/v3/index.json --api-key "$NUGET_API_KEY" --non-interactive
dotnet nuget delete Ably.PubSub.Core "$VERSION" --source https://api.nuget.org/v3/index.json --api-key "$NUGET_API_KEY" --non-interactive
```

### Release checklist

- [ ] The 2.0 docs (`README.md`, `UPDATING.md`, `CHANGELOG.md`, `CONTRIBUTING.md`, `PushNotifications.md`, `unity/README.md`) are on `main` (steps 1 and 9.6).
- [ ] The version is bumped in `src/CommonAssemblyInfo.cs`, `unity/Assets/Ably/version.txt` and the README `PackageReference` examples, and the Unity plugin is regenerated after the bump (steps 2 and 3).
- [ ] `package.yml` succeeded, and `output-package` holds exactly three `.nupkg` files and one `.unitypackage` (steps 7 and 8).
- [ ] Every door dependency group pins `Ably.PubSub.Core` as `[<version>]` (step 9.2).
- [ ] The package-install test passed on the downloaded packages (step 9.5).
- [ ] The core was indexed on nuget.org before the doors were pushed (steps 10 to 12).
- [ ] The tag and GitHub release exist, with the `.unitypackage` attached (step 14).
- [ ] The nuget.org pages render the README for all three packages, and the API key is revoked (step 16).

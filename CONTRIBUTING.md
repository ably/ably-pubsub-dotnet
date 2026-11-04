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
    - A server app, on any target framework including .NET Framework: add ably-pubsub-dotnet\src\Ably.PubSub.Core\Ably.PubSub.Core.csproj and ably-pubsub-dotnet\src\Ably.PubSub.Server\Ably.PubSub.Server.csproj.
    - A device app, on any target framework including .NET Framework: add ably-pubsub-dotnet\src\Ably.PubSub.Core\Ably.PubSub.Core.csproj and ably-pubsub-dotnet\src\Ably.PubSub.Device\Ably.PubSub.Device.csproj.
7. In any project that you want to use the SDK in, you need to add a project reference to the door project, to do so:
    1. Find your project in Solution Explorer and expand the tree so that the Dependencies node is visible
    2. Right click Dependencies and select Add Reference
    3. In the dialogue that opens you should see a list of the projects in your solution. Check the box next to the door project you added (Ably.PubSub.Server or Ably.PubSub.Device) and click OK.

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

This library uses [semantic versioning](http://semver.org/). `Ably.PubSub.Core`, `Ably.PubSub.Device` and `Ably.PubSub.Server` are always released together, at one version, and both door packages pin the core at exactly that version.

A release has two parts: a release PR that sets the version, then the [Release](https://github.com/ably/ably-pubsub-dotnet/actions/workflows/release.yml) workflow, which publishes the packages to nuget.org, tags the release and creates the GitHub release. The examples below release 2.0.1.

### Prepare the release PR

1. Create a branch named like `release/2.0.1`.
2. Set the new version in `src/CommonAssemblyInfo.cs` (`AssemblyVersion`, `AssemblyFileVersion` and `AssemblyInformationalVersion`) and in `unity/Assets/Ably/version.txt`. For a prerelease such as `2.0.1-rc.1`, `AssemblyVersion` and `AssemblyFileVersion` take `2.0.1`; `AssemblyInformationalVersion` and `version.txt` take `2.0.1-rc.1`. Commit the changes.
3. Regenerate the Unity plugin from the repository root: `./unity-plugins-updater.sh 2.0.1` on macOS or Linux, or `.\unity-plugins-updater.cmd 2.0.1` on Windows. Commit the generated `unity/Assets/Ably/Plugins/Ably.PubSub.Device.dll`.
4. Update the [CHANGELOG](./CHANGELOG.md) with [`github_changelog_generator`](https://github.com/github-changelog-generator/github-changelog-generator). This may need some manual work:
    - Run something like `github_changelog_generator -u ably -p ably-pubsub-dotnet --since-tag 2.0.0 --output delta.md --token $GITHUB_TOKEN_WITH_REPO_ACCESS`. Create the token in [GitHub personal access token settings](https://github.com/settings/tokens/new?description=GitHub%20Changelog%20Generator%20token).
    - `--output delta.md` writes the changes since `--since-tag` to a new file. Insert its contents at the top of `CHANGELOG.md`, and replace the "Unreleased" heading and links with the new version.
    - Point the "Full Changelog" link at the new version tag instead of `HEAD`.
    - The heading must read `## [2.0.1](https://github.com/ably/ably-pubsub-dotnet/tree/2.0.1) (yyyy-mm-dd)`, dated with the intended release date. The section must not be empty: it becomes the GitHub release notes.
5. Commit the changelog: `git add CHANGELOG.md && git commit -m "Update change log."`.
6. Check the branch locally. This needs the .NET SDK and Cake, as for any build (see [Cake build scripts](./cake-build/README.md)):

    ```bash
    dotnet tool restore
    dotnet cake cake-build/release.cake -- --target=Release.Preflight --version=2.0.1
    ```

    It runs the release workflow's pre-checks: the version format, the top CHANGELOG entry, and that 2.0.1 isn't on nuget.org, tagged or released yet. Fix anything it reports. "Release targets" in the [Cake build scripts](./cake-build/README.md#release-targets) README describes the options.
7. Push the branch and open the release PR. Add an SDK Team Engineering Lead and the SDK Team Product Manager as reviewers. Once it's approved, merge it to `main`.

### Publish

The Release workflow runs only on `main`. Its `publish` job waits for a reviewer of the `nuget` environment to approve it, so make sure one is available.

1. Do a dry run. Go to the [Release](https://github.com/ably/ably-pubsub-dotnet/actions/workflows/release.yml) workflow in the Actions tab, click `Run workflow`, choose `main`, enter the version and leave `dry_run` ("Check, pack and test only. Push nothing, create no tag or release.") ticked. Or use the GitHub CLI:

    ```bash
    gh workflow run release.yml --ref main -f version=2.0.1 -f dry_run=true
    ```

    It runs the same pre-checks, then packs with the [Package ably](https://github.com/ably/ably-pubsub-dotnet/actions/workflows/package.yml) workflow, which checks the version files and the Unity plugin and runs the package-install test. Then it checks the contents of the `.unitypackage`. Nothing is pushed.
2. Open the dry run's summary. It lists `Ably.PubSub.Core.2.0.1.nupkg`, `Ably.PubSub.Device.2.0.1.nupkg`, `Ably.PubSub.Server.2.0.1.nupkg` and `ably.pubsub.2.0.1.unitypackage` with their SHA-256 values, and shows the release notes. Check them.
3. Start the real run, with `dry_run` unticked:

    ```bash
    gh workflow run release.yml --ref main -f version=2.0.1 -f dry_run=false
    ```

    It repeats the checks and the packing, then the `publish` job waits for approval.
4. Ask a reviewer of the `nuget` environment to approve the run (see "Approving a release"). You can't approve a run you started.
5. Once approved, the workflow:
    - pushes `Ably.PubSub.Core`, waits until nuget.org serves it, then pushes `Ably.PubSub.Device` and `Ably.PubSub.Server`;
    - creates the annotated tag `2.0.1` on the released commit;
    - creates the GitHub release, with the CHANGELOG section as its notes and `ably.pubsub.2.0.1.unitypackage` attached. A version with a prerelease suffix, such as `2.0.1-rc.1`, is marked as a prerelease.

### Approving a release

Only the reviewers of the `nuget` environment can approve, and not for a run they started. Before approving:

1. Check the run was started on `main`, with the version the release PR intended, and with `dry_run` off.
2. Check a dry run of the same version on the same commit passed. Its summary lists the three `.nupkg` files and the `.unitypackage` with SHA-256 values.
3. Look at CI on the commit (the checks on the merge commit on `main`). The release workflow doesn't gate on CI, because some suites are flaky: the sandbox integration tests and the UTS `proxy` job can fail for reasons unrelated to the release. A known flaky failure isn't a reason to stop. A real regression or a failing build is: reject the deployment and fix `main` first.
4. Approve the `nuget` deployment from the run's page ("Review deployments").

### After the release

1. Check that [Ably.PubSub.Core](https://www.nuget.org/packages/Ably.PubSub.Core), [Ably.PubSub.Device](https://www.nuget.org/packages/Ably.PubSub.Device) and [Ably.PubSub.Server](https://www.nuget.org/packages/Ably.PubSub.Server) show 2.0.1 on nuget.org, and that the [GitHub release](https://github.com/ably/ably-pubsub-dotnet/releases) has the notes and the `.unitypackage`.
2. Create the entry on the [Ably Changelog](https://changelog.ably.com/) (via [headwayapp](https://headwayapp.co/)).

### If something goes wrong

If the `publish` job fails, its run summary lists the next steps too. What to do depends on where the run stopped:

| Where it stopped | What to do |
|---|---|
| Before the approval (pre-checks, packing, the summary) | Nothing was published. Fix the problem on `main` with a PR, then start a new run. |
| At a `NuGet/login` step (401 or 400) | Nothing was published. Check the `NUGET_USER` secret of the `nuget` environment (the nuget.org username of the person who created the "Ably PubSub" Trusted Publishing policy, not `ably-realtime`), that the policy is active, and the environment name. Then use **Re-run failed jobs** on the same run. |
| After `Ably.PubSub.Core` was pushed (the wait, or the door pushes) | Use **Re-run failed jobs** on the same run. GitHub allows it for 30 days and up to 50 times, and it may ask for approval again. The Core push is skipped as a duplicate, then the doors go out. Don't start a new run: its pre-checks refuse a version that is already partly on nuget.org. |
| `Ably.PubSub.Core` never appears on nuget.org | It probably failed nuget.org validation, and a re-run would time out again. Check the package on nuget.org and the owner's email. The version can't be reused, so release the next patch version with a new release PR. The doors were never pushed. |
| At the tag or GitHub release step | The packages are published. Use **Re-run failed jobs**. A tag already on the released commit is reused, the `.unitypackage` is uploaded only if the release lacks it, and a leftover draft release is published. |
| The tag exists on another commit | Only possible if `main` was force-moved. Delete the release and the tag (`gh release delete 2.0.1 --cleanup-tag --yes`, or `git push origin :refs/tags/2.0.1`), then re-run the `github-release` job. |
| The run can't be re-run (for example, a bug in the workflow itself) | Finish the release by hand, as in "Manual fallback": push the packages that are missing, then create the tag and the release. |
| A published version is bad | See "Withdrawing a version". |

### Manual fallback

Use this only when the Release workflow can't be used.

1. Get the packages. Download the `output-package` artifact of the release run, or run the [Package ably](https://github.com/ably/ably-pubsub-dotnet/actions/workflows/package.yml) workflow on `main` with the version and download its `output-package`. It holds `Ably.PubSub.Core.2.0.1.nupkg`, `Ably.PubSub.Device.2.0.1.nupkg`, `Ably.PubSub.Server.2.0.1.nupkg` and `ably.pubsub.2.0.1.unitypackage`.
2. Create a nuget.org API key for this release only: package owner `ably-realtime`, the "Push only new package versions" scope, glob `Ably.PubSub.*`, and the shortest expiry. nuget.org limits new keys to 30 days, so create a fresh one each time. Delete it when you are done.
3. Push the packages one at a time, `Ably.PubSub.Core` first:

    ```bash
    dotnet nuget push Ably.PubSub.Core.2.0.1.nupkg --api-key YOUR_API_KEY --source https://api.nuget.org/v3/index.json --skip-duplicate
    ```

    Once `Ably.PubSub.Core` 2.0.1 is listed on [nuget.org](https://www.nuget.org/packages/Ably.PubSub.Core), push `Ably.PubSub.Device.2.0.1.nupkg` and then `Ably.PubSub.Server.2.0.1.nupkg` the same way. Don't push with a wildcard such as `Ably.PubSub.*.nupkg`: the door packages depend on that exact core version, so the core must be published first. See [publishing with the .NET CLI](https://learn.microsoft.com/en-us/nuget/quickstart/create-and-publish-a-package-using-visual-studio?tabs=netcore-cli#publish-with-the-net-cli-or-nuget-cli) for details.
4. Create an annotated tag on the released commit and push it:

    ```bash
    git tag -a 2.0.1 -m "Ably.PubSub.Core, Ably.PubSub.Device and Ably.PubSub.Server 2.0.1" <commit>
    git push origin 2.0.1
    ```

5. Create the GitHub release with the CHANGELOG section as its notes and the Unity package attached. From the repository root, take `release-notes.md` from the release run's `release-notes` artifact, or write it with the first command below:

    ```bash
    dotnet cake cake-build/release.cake -- --target=Release.Notes --version=2.0.1 --output=release-notes.md
    gh release create 2.0.1 <path-to-output-package>/ably.pubsub.2.0.1.unitypackage --verify-tag --title 2.0.1 --notes-file release-notes.md
    ```

### Withdrawing a version

nuget.org never deletes a package, and a version number can't be reused. To withdraw a bad version:

1. Unlist `Ably.PubSub.Server` and `Ably.PubSub.Device` first, then `Ably.PubSub.Core`, so that no door is ever resolvable without its pinned core. Use the "Listing" tab of each package on nuget.org, or:

    ```bash
    dotnet nuget delete <package-id> 2.0.1 --source https://api.nuget.org/v3/index.json --api-key YOUR_API_KEY --non-interactive
    ```

    On nuget.org this unlists the version. The key needs the "Unlist package" scope, which the Trusted Publishing policy doesn't grant: create a short-lived key as in "Manual fallback", with that scope instead of push.
2. Optionally deprecate the version on nuget.org.
3. Release a fixed patch version.
4. Keep the tag and the GitHub release, and edit the release notes to say that the version is withdrawn.

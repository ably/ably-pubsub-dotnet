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

If you want to incorporate `ably-dotnet` into your project from source (perhaps to use a specific development branch) the simplest way to do so is to add references to the relevant ably-dotnet projects. The following steps are specific to Visual Studio 2019, but the principal should transfer to other IDEs

1. Clone this repository to your local system (`git clone --recurse-submodules https://github.com/ably/ably-dotnet.git`)
2. Open the solution you want to reference ably-dotnet from
3. In Solution Explorer right click the root node (it will be labelled Solution 'YourSolutionName')
4. Select Add > Existing Project from the context menu
5. Browse to the ably-dotnet repository and add ably-dotnet\src\Ably.PubSub.Shared\Ably.PubSub.Shared.shproj
6. Browse to the ably-dotnet repository and add the project that corresponds to your target platform, so if you are targeting .NET Framework (AKA Classic .NET) you would add ably-dotnet\src\Ably.PubSub.Core.NETFramework\Ably.PubSub.Core.NETFramework.csproj, and for .NET / .NET Standard you would add ably-dotnet\src\Ably.PubSub.Core\Ably.PubSub.Core.csproj.
7. In any project that you want to use `ably-dotnet` you need to add a project reference, to do so:
    1. Find your project in Solution Explorer and expand the tree so that the Dependencies node is visible
    2. Right click Dependencies and select Add Reference
    3. In the dialogue that opens you should see a list of the projects in your solution. Check the box next to Ably.PubSub.Core.NETFramework (or Ably.PubSub.Core, whichever you added) and click OK.

## Spec

The dotnet library follows the Ably [Features spec](https://docs.ably.com/client-lib-development-guide/features/). To ensure it is easier to look up whether a spec item has been implemented or not; we add a Trait attribute to tests that implement parts of the spec. The convention is to add `[Trait("spec", "spec tag")]` to unit tests.

To get a list of all spec items that appear in the tests you can run a script located in the tools directory.
You need to have .NET Core 3.1 installed. It works on Mac, Linux and Windows. Run `dotnet fsi tools/list-test-categories.fsx`. It will produce a `results.csv` file which will include all spec items, which file it was found and on what line.

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

A technique that has proved extremely successful when it comes to tracking down sporadic communication failures is to enable ‘break on throw’ in your debugger. The fact is that the exception handling logic in the Ably .NET SDK exhibits overly generous catch clauses which tend to catch too broad a set of exception types. These then tend to be processed as ‘something went wrong’ (i.e. we lose valuable and actionable recovery context) or are swallowed and ignored, on the basis that retry logic will, eventually, make everything good. If the retries subsequently fail to reestablish the invariants then you will commonly see `NullReferenceExceptions` as the code higher up makes assumptions about what state the object graph is in. In general if you see a `NullReferenceException` it is worth hunting down its source and precipitating conditions since it’s likely being used now as some kind of flow-of-control signal that triggers retry logic.

#### Debugging Tip: Debug and Release execution traces can differ

_More detail to follow around this._

### Testing

Testing takes place primarily via GitHub Actions based infrastructure, here Windows, Linux and macOS assemblies are built, and integration tests are run. In addition to this support is being added so that the GitHub repository can orchestrate a remote Unity build via [Unity’s Cloud Build service](https://unity.com/products/cloud-build). There are various reasons why, in addition to GitHub’s infrastructure (for free, because this is an open, public repository), the paid-for Unity Cloud Build service is used:

1. **Physical license server**: Currently Unity requires a license server to be run on physical hardware. The license server is needed to securely allocate a temporary license to a GitHub Worker. We do not wish to provision our own dedicated physical or virtual license service instances, thus pointing us in the direction of Unity Cloud Build.

2. **Need to build for multiple platforms**: A huge part of the Unity value proposition is that Unity runs on just about every commercially significant gaming platform. It would not be financially or practically possible for Ably to replicate even a modest subset of this.

3. **Existing concerns with the stability of the GitHub infrastructure**: Currently our GitHub workflows test Windows, Linux and macOS. The root cause of this is bound up in the fact that the vast majority of our tests are integration tests that run against the remotely hosted Ably sandbox. Network uncertainty, combined with wildly varying runner performance leads to frequent timeouts and associated protocol failures. Until or if we ever manage to reign these in it would be foolish to try and add additional platforms to the existing GitHub infrastructure.

At the present time it is possible for tests to transition from green to red with no code changes on the part of the SDK team. There are two reasons for this:

1. **Unexpected changes to the sandbox**: These tend to occur when the sandbox either evolves ahead of the Ably API specification or improves its conformance to an existing specification point which then subsequently exposes a weak implementation on the client side, in this case within the Ably .NET SDK.

2. **Uncontrolled dependency evolution**: At the present time the majority of the 3rd party Ably .NET dependencies have open dependency requirements which is to say we specify a minimum but not a maximum or even discrete list of versions against which we certify and support conformance. For example, if the Newtonsoft Json .NET library is updated it will be used by our next build and may contribute to test failures.

When one or more Ably .NET test failures is observed it is worth checking against another Ably client SDK with similar capabilities to see if it is also suffering from similar failures. This is aided somewhat by the fact that tests are decorated (the specifics vary depending on the SDK) with metadata that points to the Ably Specification point the test was originally intended to verify conformance for. So, for example if it is observed that the Channel Presence tests appear to be failing for .NET then check to see if the Ably Go SDK is showing failures in the same area. It is not always enough to just check to see if the Ably Go SDK happens to be gree as this just means it was green at the time the last pull request was created, updated or merged.

As touched upon above, the recommended hardware for replicating cross platform test failures is at least an Intel based MacBook Pro. Ideally the developer should have access to both Intel and Apple Silicon (ARM64) hardware. ARM based CLR implementations are not a 1:1 substitute for older, more mature Intel based implementations at least at the current time. You need both and will observe behavioral differences in test execution when comparing one platform against the other.

As of Windows 11, mainstream Windows on ARM is increasingly A Thing and can not be ignored, it’s not just Apple and Linux who are embracing non x86 native instruction sets.

Experience has shown you are better off running local VMs for guest OS images as their cloud-based alternatives suffer from lack of configuration and performance variability.

Note: it was recently decided to remove explicit mention of Windows 7 as a supported platform for the Ably .NET SDK. It is probably worth considering when we can also look to exclude Windows 8 and Windows 8.1 setting the minimum supported version to Windows 10.

## Release process

>Important Note 
>- If local dotnet environment + project setup is not available, please use [github codespaces](https://github.com/codespaces) instead.
>- We have created `.devcontainer` folder at root specifying necessary dependencies for environment setup needed for release process.
>- Visit [ably-dotnet](https://github.com/ably/ably-dotnet) repo, click on `Code` button at the right corner and create codespace for current branch from `Codespaces` tab. It will take some time to create the environment. Once codespace is created for the project, you can proceed with release steps mentioned below.

This library uses [semantic versioning](http://semver.org/).

**`Ably.PubSub.Core`, `Ably.PubSub.Device` and `Ably.PubSub.Server` are released together, always, at one
version.** Each door package declares an exact dependency on `[<that version>]` of the core, so a door
released without its core, or at a different version from it, is unresolvable for consumers. There is
therefore no such thing as releasing one of the three: one version number is bumped in one commit, and one
dispatch of `publish.yml` publishes all three in the order core → device → server.

The guards are in the build, not in this document. `Release.Preflight` refuses to release unless the
version input equals both version files, `nuget/` holds exactly the three packages, and every door pins the
core exactly; after the pack, `Release.VerifyPackages` opens each `.nupkg` and asserts the packed pin. The
[Release dry run](https://github.com/ably/ably-dotnet/actions/workflows/release-dry-run.yml) job runs both
on every pull request, so any of this being wrong is a red PR, not a bad release.

For each release:

1. Create a branch for the release, named like release/2.0.1 (where 2.0.1 is the new version number).
2. Replace all references of the current version number with the new version number and commit the changes. There are exactly two files: `src/CommonAssemblyInfo.cs` (all three assembly version attributes) and `unity/Assets/Ably/version.txt`. Every nuspec takes its version, and each door its core pin, from the release input, so nothing else is edited.
3. Run `./unity-plugins-updater.sh 2.0.1` (linux/mac) / `.\unity-plugins-updater.cmd 2.0.1` (windows) at root and commit the generated `.dll` and `.pdb` files. (Needs Mono or Windows.)
4. Optionally check the release locally before pushing: `./build.sh -- --target=Release.Preflight --version=2.0.1`. Note the `--`: Cake reserves `--version` for itself, so arguments for the build script go after it. A full local pack (`./package.cmd 2.0.1`) needs Windows or Mono, because the core and server packages carry a `lib/net46` asset.
5. Run [`github_changelog_generator`](https://github.com/github-changelog-generator/github-changelog-generator) to automate the update of the [CHANGELOG](./CHANGELOG.md). This may require some manual intervention, both in terms of how the command is run and how the change log file is modified. Your mileage may vary:
  - The command you will need to run will look something like this: `github_changelog_generator -u ably -p ably-dotnet --since-tag 2.0.0 --output delta.md --token $GITHUB_TOKEN_WITH_REPO_ACCESS`. Generate token [here](https://github.com/settings/tokens/new?description=GitHub%20Changelog%20Generator%20token).
  - Using the command above, `--output delta.md` writes changes made after `--since-tag` to a new file.
  - The contents of that new file (`delta.md`) then need to be manually inserted at the top of the `CHANGELOG.md`, changing the "Unreleased" heading and linking with the current version numbers.
  - Also ensure that the "Full Changelog" link points to the new version tag instead of the `HEAD`.
6. Commit this change: `git add CHANGELOG.md && git commit -m "Update change log."`.
7. Push the branch and create a release PR (ensure you include an SDK Team Engineering Lead and the SDK Team Product Manager as reviewers) and gain approvals for it, then merge it to the release branch. The `release-dry-run` check on that PR is the packaging gate: it packs all three packages from your commit and asserts them. Until the 2.0 integration branch merges to `main`, the release branch is `integration/v2`; after that it is `main`.
8. **Dry run.** Dispatch the [Publish](https://github.com/ably/ably-dotnet/actions/workflows/publish.yml) workflow with `version` = the new version, `dry_run` = **true**, against the ref you just merged to:

    ```
    gh workflow run publish.yml --ref integration/v2 -f version=2.0.1 -f dry_run=true
    ```

    (Or use `Run workflow` in the Actions tab. `workflow_dispatch` reads the workflow file from the default branch but runs it against the ref you choose.) This builds, packs, asserts and prints exactly what it would push, and pushes nothing. Read the output.
9. **Publish.** Dispatch the same workflow again with `dry_run` = **false**. It publishes core → device → server, waiting for the core version to be listed on nuget.org before the doors that pin it go out, skipping any version already published, and then creates the `2.0.1` tag and GitHub release with the three `.nupkg` files and the `.unitypackage` attached. No API key is involved: the workflow authenticates with nuget.org Trusted Publishing over GitHub OIDC.
    - **If it fails partway, re-run it with the same version.** Every push is skipped if that version is already on nuget.org, and the GitHub release is created only if it does not already exist, so a re-run completes the release instead of duplicating it. Do not push anything by hand.
    - **If the release has to be abandoned:** NuGet cannot delete a published version and the number can never be reused. The workflow prints the `dotnet nuget delete` commands that *unlist* each package (doors first, then the core), and then a new version has to be released.
10. Create the entry on the [Ably Changelog](https://changelog.ably.com/) (via [headwayapp](https://headwayapp.co/)).

### nuget.org Trusted Publishing setup

`publish.yml` has no long-lived NuGet API key. It exchanges a GitHub OIDC token for an API key valid for one
hour, via the [`NuGet/login`](https://github.com/NuGet/login) action, immediately before pushing. This
requires a one-time setup on nuget.org, which someone who can administer the packages' owning account has to do:

- Sign in to nuget.org → your username → **Trusted Publishing** → add a policy, owned by the account (or
  organization) that owns the `Ably.PubSub.*` package ids, with **Repository Owner** `ably`, **Repository**
  the name of this repository, and **Workflow File** `publish.yml` (the file name only, not the path). Leave
  **Environment** empty; this workflow uses no GitHub environment.
- Set the policy's **scope** to allow publishing new packages as well as new versions, with a glob covering
  `Ably.PubSub.*`. A policy does not require the package to exist already: the "new packages" scope is how
  the first version of a brand-new id is published, which is what claims the name.
- Add a repository secret `NUGET_USER` holding the nuget.org **username** (the profile name, not an email
  address). It is not a credential; it is a secret only to keep the account name out of public logs.
- A policy on a private repository starts out *temporarily active* for 7 days and becomes permanent on the
  first successful publish, because nuget.org needs the repository and owner IDs from a real token to lock
  the policy against repository-resurrection attacks.
- The policy matches the repository by **name**, so it must be created (or re-created) **after** any rename
  of this repository.

To fall back to a long-lived API key instead: delete the `NuGet login` step in `publish.yml`, drop
`id-token: write` from that job's permissions, add a `NUGET_API_KEY` repository secret, and use it in the
push step. The workflow says so at the top, in a comment next to the step.

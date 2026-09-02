using System.Text.RegularExpressions;

///////////////////////////////////////////////////////////////////////////////
// RELEASE PRE-FLIGHT AND PACKAGE ASSERTIONS
///////////////////////////////////////////////////////////////////////////////
//
// Ably.PubSub.Core, Ably.PubSub.Device and Ably.PubSub.Server are released in
// lockstep at one version, and both door packages pin the core with an exact
// version range. Nothing about that is enforced by the build itself, so these
// checks enforce it, and they run *before* anything is produced:
//
//   _Release_Preflight       source-only assertions. Runs before _Version
//                            regenerates CommonAssemblyInfo.cs, so "the
//                            --version input matches the committed version
//                            files" is a real assertion and not a tautology.
//   _Release_Verify_Files    every <file src> glob in every nuspec resolves to
//                            at least one real file. Runs after the packaging
//                            build and before the pack, because nuget silently
//                            omits files it cannot find: a stale or partial
//                            build would otherwise ship an empty package.
//   _Release_Verify_Packages post-pack assertions against the produced .nupkg
//                            files themselves, including the packed door -> core
//                            pin, which is the only place the $version$ token
//                            substitution inside a dependency version attribute
//                            can actually be proven.
//
///////////////////////////////////////////////////////////////////////////////

// The lockstep package set: nuspec file name -> package id. This set is exact.
// A resurrected nuget/io.ably.nuspec, or a fourth package added without a
// decision, fails the pre-flight. It is also what makes publish.yml safe to
// live on the default branch while that branch is still 1.x: a 1.x checkout has
// nuget/io.ably.nuspec and none of these, so the pre-flight refuses it.
var releasePackageSet = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
{
    { "ably.pubsub.core.nuspec", "Ably.PubSub.Core" },
    { "ably.pubsub.device.nuspec", "Ably.PubSub.Device" },
    { "ably.pubsub.server.nuspec", "Ably.PubSub.Server" }
};

// The door packages. Each must pin the core at the exact version being released.
var releaseDoorNuspecs = new[] { "ably.pubsub.device.nuspec", "ably.pubsub.server.nuspec" };

// The package id the doors depend on and pin exactly.
const string CorePackageId = "Ably.PubSub.Core";

// Package ids that must never appear as a dependency of anything shipped from
// this branch. ably.io 1.x is published from the maintenance branch only, and a
// door that depended on it would put two Ably cores in one project.
var releaseForbiddenDependencyIds = new[] { "ably.io", "io.ably", "ably.io.push.android", "ably.io.push.ios" };

public DirectoryPath ReleaseNuspecDirectory()
{
    return paths.Root.Combine("nuget");
}

public DirectoryPath ReleasePackageOutputDirectory()
{
    if (string.IsNullOrEmpty(packageOutput))
    {
        return paths.Root;
    }

    // Resolved against the repository root, not Cake's working directory (which
    // is cake-build/), so --packageOutput=dry-run-packages means what it reads like.
    DirectoryPath candidate = packageOutput;
    return candidate.IsRelative ? paths.Root.Combine(candidate) : candidate;
}

public string ReleaseReadRequiredVersionArgument()
{
    if (string.IsNullOrWhiteSpace(version))
    {
        throw new Exception(
            "No --version was supplied. The release version is an explicit input so that it can be " +
            "checked against the committed version files; pass it, e.g. --version=2.0.0.");
    }

    if (!Regex.IsMatch(version, @"^\d+\.\d+\.\d+(-[0-9A-Za-z][0-9A-Za-z.-]*)?$"))
    {
        throw new Exception(
            $"--version='{version}' is not a release version. Expected MAJOR.MINOR.PATCH with an " +
            "optional prerelease suffix, e.g. 2.0.0 or 2.0.0-rc.1.");
    }

    return version;
}

///////////////////////////////////////////////////////////////////////////////
// Assertion 1: the version input and the two committed version files agree.
///////////////////////////////////////////////////////////////////////////////

public void ReleaseAssertVersionFilesAgree(string releaseVersion, List<string> errors)
{
    var assemblyInfo = paths.Src.CombineWithFilePath("CommonAssemblyInfo.cs");
    if (!FileExists(assemblyInfo))
    {
        errors.Add($"{assemblyInfo.FullPath} is missing. It is the single source of the runtime version.");
    }
    else
    {
        var assemblyInfoText = System.IO.File.ReadAllText(assemblyInfo.FullPath);

        foreach (var attribute in new[] { "AssemblyVersion", "AssemblyFileVersion", "AssemblyInformationalVersion" })
        {
            var match = Regex.Match(assemblyInfoText, attribute + @"\(""([^""]*)""\)");
            if (!match.Success)
            {
                errors.Add($"src/CommonAssemblyInfo.cs has no [assembly: {attribute}(\"...\")] attribute.");
            }
            else if (match.Groups[1].Value != releaseVersion)
            {
                errors.Add(
                    $"src/CommonAssemblyInfo.cs {attribute} is '{match.Groups[1].Value}' but --version is " +
                    $"'{releaseVersion}'. Bump the version files in their own commit and release the merged " +
                    "version; the release input never overrides what is committed.");
            }
        }
    }

    var unityVersion = paths.Root.CombineWithFilePath("unity/Assets/Ably/version.txt");
    if (!FileExists(unityVersion))
    {
        errors.Add($"{unityVersion.FullPath} is missing. The Unity package reads its version from it.");
    }
    else
    {
        var unityVersionText = System.IO.File.ReadAllText(unityVersion.FullPath).Trim();
        if (unityVersionText != releaseVersion)
        {
            errors.Add(
                $"unity/Assets/Ably/version.txt is '{unityVersionText}' but --version is '{releaseVersion}'. " +
                "Both version files must be bumped together: the .unitypackage ships from the same run as " +
                "the NuGet packages and must carry the same version.");
        }
    }
}

///////////////////////////////////////////////////////////////////////////////
// Assertion 2: the nuspec set is exactly the lockstep set, with the expected ids.
// Assertion 3: every door pins the core as [$version$] and nothing depends on
//              the 1.x package ids.
///////////////////////////////////////////////////////////////////////////////

public void ReleaseAssertNuspecs(List<string> errors)
{
    var nuspecDirectory = ReleaseNuspecDirectory();
    if (!DirectoryExists(nuspecDirectory))
    {
        errors.Add($"{nuspecDirectory.FullPath} does not exist, so there is nothing to release.");
        return;
    }

    var found = GetFiles(nuspecDirectory.FullPath + "/*.nuspec")
        .Select(f => f.GetFilename().FullPath)
        .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
        .ToList();

    var expected = releasePackageSet.Keys.OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList();

    var missing = expected.Where(e => !found.Contains(e, StringComparer.OrdinalIgnoreCase)).ToList();
    var unexpected = found.Where(f => !releasePackageSet.ContainsKey(f)).ToList();

    if (missing.Count > 0 || unexpected.Count > 0)
    {
        errors.Add(
            "nuget/ does not hold exactly the lockstep package set.\n" +
            $"    expected:   {string.Join(", ", expected)}\n" +
            $"    found:      {(found.Count == 0 ? "(none)" : string.Join(", ", found))}\n" +
            (missing.Count > 0 ? $"    missing:    {string.Join(", ", missing)}\n" : string.Empty) +
            (unexpected.Count > 0 ? $"    unexpected: {string.Join(", ", unexpected)}\n" : string.Empty) +
            "    Every package in this set is released together at one version. If this is a 1.x checkout\n" +
            "    (nuget/io.ably.nuspec), it cannot be released by this workflow: ably.io ships from the 1.x\n" +
            "    maintenance branch with its own tooling.");
        return;
    }

    foreach (var entry in releasePackageSet)
    {
        var nuspec = nuspecDirectory.CombineWithFilePath(entry.Key);
        var text = System.IO.File.ReadAllText(nuspec.FullPath);

        var idMatch = Regex.Match(text, @"<id>\s*([^<\s]+)\s*</id>");
        if (!idMatch.Success)
        {
            errors.Add($"nuget/{entry.Key} has no <id>.");
        }
        else if (idMatch.Groups[1].Value != entry.Value)
        {
            errors.Add(
                $"nuget/{entry.Key} declares <id>{idMatch.Groups[1].Value}</id> but the lockstep set expects " +
                $"'{entry.Value}'. Renaming a published package id is not a rename, it is a new package.");
        }

        var versionMatch = Regex.Match(text, @"<version>\s*([^<\s]+)\s*</version>");
        if (!versionMatch.Success || versionMatch.Groups[1].Value != "$version$")
        {
            errors.Add(
                $"nuget/{entry.Key} must declare <version>$version$</version> so that the pack version comes " +
                "from the release input alone. A hard-coded version here would let a package ship at a " +
                "different version from its siblings.");
        }

        // Every dependency in this nuspec, with its declared version range.
        var dependencies = Regex.Matches(text, @"<dependency\s+id=""(?<id>[^""]+)""\s+version=""(?<version>[^""]*)""")
            .Cast<Match>()
            .Select(m => new { Id = m.Groups["id"].Value, Version = m.Groups["version"].Value })
            .ToList();

        foreach (var forbidden in releaseForbiddenDependencyIds)
        {
            if (dependencies.Any(d => string.Equals(d.Id, forbidden, StringComparison.OrdinalIgnoreCase)))
            {
                errors.Add(
                    $"nuget/{entry.Key} depends on '{forbidden}'. The 1.x packages are never a dependency of " +
                    "the 2.0 set: a project that resolved both would have two Ably cores in it.");
            }
        }

        var isDoor = releaseDoorNuspecs.Contains(entry.Key, StringComparer.OrdinalIgnoreCase);
        var corePins = dependencies
            .Where(d => string.Equals(d.Id, CorePackageId, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (isDoor)
        {
            var groupCount = Regex.Matches(text, @"<group\s+targetFramework=").Count;

            if (corePins.Count == 0)
            {
                errors.Add(
                    $"nuget/{entry.Key} is a door package but declares no dependency on {CorePackageId}.");
            }
            else if (corePins.Count != groupCount)
            {
                errors.Add(
                    $"nuget/{entry.Key} has {groupCount} dependency group(s) but {corePins.Count} " +
                    $"{CorePackageId} dependencies. Every target framework group must pin the core, or the " +
                    "framework whose group omits it resolves whatever core version it likes.");
            }

            foreach (var pin in corePins.Where(p => p.Version != "[$version$]"))
            {
                errors.Add(
                    $"nuget/{entry.Key} pins {CorePackageId} as version=\"{pin.Version}\", expected the exact " +
                    "range token \"[$version$]\". Square brackets are what make it exact: '2.0.0' without " +
                    "them is a minimum, so a consumer could silently resolve a newer core than the door was " +
                    "built and tested against, and two doors could pull two different cores into one project.");
            }
        }
        else if (corePins.Count > 0)
        {
            errors.Add($"nuget/{entry.Key} is the core package but depends on {CorePackageId}.");
        }
    }
}

///////////////////////////////////////////////////////////////////////////////
// Assertion 5: every <file src> glob resolves to at least one real file.
///////////////////////////////////////////////////////////////////////////////

public void ReleaseAssertNuspecFilesResolve(List<string> errors)
{
    var nuspecDirectory = ReleaseNuspecDirectory();

    foreach (var nuspecName in releasePackageSet.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase))
    {
        var nuspec = nuspecDirectory.CombineWithFilePath(nuspecName);
        if (!FileExists(nuspec))
        {
            continue; // already reported by the nuspec set assertion
        }

        var text = System.IO.File.ReadAllText(nuspec.FullPath);

        foreach (Match file in Regex.Matches(text, @"<file\s+src=""(?<src>[^""]+)""\s+target=""(?<target>[^""]*)""\s*/>"))
        {
            var src = file.Groups["src"].Value
                .Replace("$configuration$", configuration)
                .Replace('\\', '/');

            var pattern = nuspecDirectory.Combine(System.IO.Path.GetDirectoryName(src) ?? string.Empty)
                .CombineWithFilePath(System.IO.Path.GetFileName(src));
            var absolute = MakeAbsolute(pattern).FullPath;

            var resolved = absolute.Contains("*")
                ? GetFiles(absolute).Count
                : (System.IO.File.Exists(absolute) ? 1 : 0);

            if (resolved == 0)
            {
                errors.Add(
                    $"nuget/{nuspecName}: <file src=\"{file.Groups["src"].Value}\"> matches nothing " +
                    $"({absolute}). nuget does not fail on a files entry that matches nothing, it silently " +
                    "omits it, so a stale or partial build would ship a package missing that asset. Build " +
                    $"the {configuration} configuration of every head first.");
            }
        }
    }
}

///////////////////////////////////////////////////////////////////////////////
// Assertion 4: post-pack assertions against the produced .nupkg files.
///////////////////////////////////////////////////////////////////////////////

public void ReleaseAssertPackedPackages(string releaseVersion, List<string> errors)
{
    var outputDirectory = ReleasePackageOutputDirectory();
    var extractRoot = paths.Root.Combine("test-results/release-verify");

    if (DirectoryExists(extractRoot))
    {
        DeleteDirectory(extractRoot, new DeleteDirectorySettings { Recursive = true, Force = true });
    }
    CreateDirectory(extractRoot);

    foreach (var entry in releasePackageSet.OrderBy(e => e.Key, StringComparer.OrdinalIgnoreCase))
    {
        var packageId = entry.Value;
        var nupkg = outputDirectory.CombineWithFilePath($"{packageId}.{releaseVersion}.nupkg");

        if (!FileExists(nupkg))
        {
            errors.Add(
                $"{nupkg.FullPath} was not produced. All {releasePackageSet.Count} packages of the lockstep " +
                "set are packed from one run; a missing one means the release is partial before it even " +
                "reaches the registry.");
            continue;
        }

        var extracted = extractRoot.Combine(packageId);
        Unzip(nupkg, extracted);

        var packedNuspec = GetFiles(extracted.FullPath + "/*.nuspec").FirstOrDefault();
        if (packedNuspec == null)
        {
            errors.Add($"{nupkg.GetFilename()} contains no .nuspec.");
            continue;
        }

        var packedText = System.IO.File.ReadAllText(packedNuspec.FullPath);

        var packedId = Regex.Match(packedText, @"<id>\s*([^<\s]+)\s*</id>").Groups[1].Value;
        if (packedId != packageId)
        {
            errors.Add($"{nupkg.GetFilename()} declares id '{packedId}', expected '{packageId}'.");
        }

        var packedVersion = Regex.Match(packedText, @"<version>\s*([^<\s]+)\s*</version>").Groups[1].Value;
        if (packedVersion != releaseVersion)
        {
            errors.Add(
                $"{nupkg.GetFilename()} declares version '{packedVersion}', expected '{releaseVersion}'. " +
                "The $version$ token did not substitute.");
        }

        var isDoor = releaseDoorNuspecs.Contains(entry.Key, StringComparer.OrdinalIgnoreCase);
        if (isDoor)
        {
            var packedPins = Regex.Matches(packedText, @"<dependency\s+id=""(?<id>[^""]+)""\s+version=""(?<version>[^""]*)""")
                .Cast<Match>()
                .Where(m => string.Equals(m.Groups["id"].Value, CorePackageId, StringComparison.OrdinalIgnoreCase))
                .Select(m => m.Groups["version"].Value)
                .ToList();

            if (packedPins.Count == 0)
            {
                errors.Add($"{nupkg.GetFilename()} has no packed {CorePackageId} dependency.");
            }

            foreach (var pin in packedPins)
            {
                // This is the assertion the whole exact-pin design rests on: the
                // $version$ token has to substitute inside a dependency's version
                // attribute, not just in <version>. If it did not, the pin would
                // ship as the literal "[$version$]" or as an empty range.
                if (pin != $"[{releaseVersion}]")
                {
                    errors.Add(
                        $"{nupkg.GetFilename()} pins {CorePackageId} as '{pin}', expected " +
                        $"'[{releaseVersion}]'. The exact pin is what stops a consumer resolving a core " +
                        "version this door was never built against.");
                }
                else
                {
                    Information($"  {packageId}: packed pin <dependency id=\"{CorePackageId}\" version=\"{pin}\" />");
                }
            }
        }

        // Every lib/<tfm> the source nuspec targets must actually contain the
        // package's own assembly in the produced .nupkg.
        var sourceText = System.IO.File.ReadAllText(ReleaseNuspecDirectory().CombineWithFilePath(entry.Key).FullPath);
        var libTargets = Regex.Matches(sourceText, @"target=""lib[\\/](?<tfm>[^""\\/]+)""")
            .Cast<Match>()
            .Select(m => m.Groups["tfm"].Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (libTargets.Count == 0)
        {
            errors.Add($"nuget/{entry.Key} declares no lib/<tfm> targets.");
        }

        var packedFiles = GetFiles(extracted.FullPath + "/**/*")
            .Select(f => MakeAbsolute(f).FullPath.Substring(MakeAbsolute(extracted).FullPath.Length + 1).Replace('\\', '/'))
            .ToList();

        foreach (var tfm in libTargets)
        {
            var expectedAssembly = $"lib/{tfm}/{packageId}.dll";
            if (!packedFiles.Any(f => string.Equals(f, expectedAssembly, StringComparison.OrdinalIgnoreCase)))
            {
                errors.Add(
                    $"{nupkg.GetFilename()} has no {expectedAssembly}. The nuspec targets lib/{tfm}, so the " +
                    $"{tfm} head either did not build or its files entry matched nothing.");
            }
        }

        Information($"  {packageId} {packedVersion}: {packedFiles.Count} files, lib/{string.Join(", lib/", libTargets)}");
    }
}

///////////////////////////////////////////////////////////////////////////////
// TASKS (Internal)
///////////////////////////////////////////////////////////////////////////////

Task("_Release_Preflight")
    .Description("Assert the release is coherent before anything is built, packed or pushed")
    .Does(() =>
{
    var releaseVersion = ReleaseReadRequiredVersionArgument();

    Information($"Release pre-flight for {releaseVersion}");
    Information($"  packages: {string.Join(", ", releasePackageSet.Values.OrderBy(v => v))}");

    var errors = new List<string>();

    ReleaseAssertVersionFilesAgree(releaseVersion, errors);
    ReleaseAssertNuspecs(errors);

    if (errors.Count > 0)
    {
        throw new Exception(
            $"Release pre-flight failed with {errors.Count} problem(s); nothing has been built, packed or " +
            "pushed:\n\n  - " + string.Join("\n\n  - ", errors) + "\n");
    }

    Information("Release pre-flight passed:");
    Information($"  --version, src/CommonAssemblyInfo.cs and unity/Assets/Ably/version.txt all say {releaseVersion}");
    Information($"  nuget/ holds exactly the lockstep set and each door pins {CorePackageId} as [$version$]");
});

Task("_Release_Verify_Files")
    .Description("Assert every nuspec files entry resolves, so no package can ship empty")
    .Does(() =>
{
    var errors = new List<string>();
    ReleaseAssertNuspecFilesResolve(errors);

    if (errors.Count > 0)
    {
        throw new Exception(
            $"Nuspec files verification failed with {errors.Count} problem(s); nothing has been packed:\n\n  - " +
            string.Join("\n\n  - ", errors) + "\n");
    }

    Information($"Every nuspec <file src> glob resolves against the {configuration} build output.");
});

Task("_Release_Verify_Packages")
    .Description("Assert the produced .nupkg files carry the right version and the exact core pin")
    .Does(() =>
{
    var releaseVersion = ReleaseReadRequiredVersionArgument();

    Information($"Verifying packed packages in {ReleasePackageOutputDirectory().FullPath}");

    var errors = new List<string>();
    ReleaseAssertPackedPackages(releaseVersion, errors);

    if (errors.Count > 0)
    {
        throw new Exception(
            $"Packed package verification failed with {errors.Count} problem(s); do not publish these " +
            "artifacts:\n\n  - " + string.Join("\n\n  - ", errors) + "\n");
    }

    Information($"All {releasePackageSet.Count} packages are at {releaseVersion} and both doors pin " +
                $"{CorePackageId} as [{releaseVersion}].");
});

///////////////////////////////////////////////////////////////////////////////
// PUBLIC TARGETS
///////////////////////////////////////////////////////////////////////////////

Task("Release.Preflight")
    .Description("Run the release pre-flight assertions on the working tree (no build, no pack, no push)")
    .IsDependentOn("_Release_Preflight");

Task("Release.VerifyPackages")
    .Description("Run the post-pack assertions against .nupkg files already produced")
    .IsDependentOn("_Release_Verify_Packages");

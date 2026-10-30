using System.Text.Json;
using System.Text.RegularExpressions;

///////////////////////////////////////////////////////////////////////////////
// PACKAGE-CONSUMPTION TEST
///////////////////////////////////////////////////////////////////////////////
//
// The unit tests build against project references, which bypass NuGet's
// per-target-framework asset selection and the Package-only signing, so a
// problem that exists only in the shipped .nupkg files (a door asset paired with
// a core asset that does not grant it internals access, a missing lib/<tfm>, a
// wrong keyed InternalsVisibleTo) is invisible to them. This test consumes the
// packed packages the way a customer does:
//
//   1. copies the apps in tests/PackageConsumption to test-results/package-consumption;
//   2. writes a nuget.config that restores Ably.PubSub.* only from the packed
//      .nupkg files (package source mapping) and everything else from
//      nuget.org, into a private global packages folder, so neither a stale
//      local cache nor a published package of the same version can stand in;
//   3. restores and builds each app for every target framework in the matrix;
//   4. asserts, from obj/project.assets.json, which lib/<tfm> asset of each Ably
//      package NuGet selected, and that the Ably packages came from the local
//      folder;
//   5. runs each app on its runtime (.NET Framework apps on the .NET Framework
//      CLR on Windows and on Mono elsewhere). The apps create clients through
//      their door and call into the core, offline: no credentials, no network.
//
///////////////////////////////////////////////////////////////////////////////

// THE MATRIX: the one place to change when the package layout changes. Each row
// is an app, a target framework that app is built for, and the lib/<tfm> folder
// NuGet is expected to select from each Ably package for it. A row whose
// expected folder is not declared by that package's nuspec fails the run, so
// the matrix cannot drift from the nuspecs silently.
var packageConsumptionMatrix = new[]
{
    // A .NET Framework device app gets the netstandard2.0 Device door with the net46 core.
    new PackageConsumptionCase("DeviceApp", "net462", ("Ably.PubSub.Device", "netstandard2.0"), ("Ably.PubSub.Core", "net46")),
    new PackageConsumptionCase("DeviceApp", "net48", ("Ably.PubSub.Device", "netstandard2.0"), ("Ably.PubSub.Core", "net46")),
    new PackageConsumptionCase("DeviceApp", "net6.0", ("Ably.PubSub.Device", "net6.0"), ("Ably.PubSub.Core", "net6.0")),
    new PackageConsumptionCase("DeviceApp", "net7.0", ("Ably.PubSub.Device", "net7.0"), ("Ably.PubSub.Core", "net7.0")),
    new PackageConsumptionCase("ServerApp", "net462", ("Ably.PubSub.Server", "net46"), ("Ably.PubSub.Core", "net46")),
    new PackageConsumptionCase("ServerApp", "net48", ("Ably.PubSub.Server", "net46"), ("Ably.PubSub.Core", "net46")),
    new PackageConsumptionCase("ServerApp", "net6.0", ("Ably.PubSub.Server", "net6.0"), ("Ably.PubSub.Core", "net6.0")),
    new PackageConsumptionCase("ServerApp", "net7.0", ("Ably.PubSub.Server", "net7.0"), ("Ably.PubSub.Core", "net7.0")),
};

public class PackageConsumptionCase
{
    public PackageConsumptionCase(string app, string targetFramework, params (string PackageId, string Lib)[] expectedAssets)
    {
        App = app;
        TargetFramework = targetFramework;
        ExpectedAssets = expectedAssets;
    }

    public string App { get; }

    public string TargetFramework { get; }

    public (string PackageId, string Lib)[] ExpectedAssets { get; }

    // net462, net48, net481: an SDK-style .NET Framework target, run on the CLR or Mono.
    public bool IsNetFramework => Regex.IsMatch(TargetFramework, @"^net\d{2,3}$");

    public string Name => $"{App} {TargetFramework}";
}

// project.assets.json keys a target either by the alias used in <TargetFrameworks>
// (net6.0) or by its full framework name (.NETFramework,Version=v4.8), depending
// on the framework and the NuGet version, so look a target up under both.
public string[] PackageConsumptionTargetKeys(string targetFramework)
{
    var keys = new List<string> { targetFramework };

    var netFramework = Regex.Match(targetFramework, @"^net(\d)(\d)(\d)?$");
    if (netFramework.Success)
    {
        var patch = netFramework.Groups[3].Success ? $".{netFramework.Groups[3].Value}" : string.Empty;
        keys.Add($".NETFramework,Version=v{netFramework.Groups[1].Value}.{netFramework.Groups[2].Value}{patch}");
    }

    var netCore = Regex.Match(targetFramework, @"^net(?:coreapp)?(\d+\.\d+)$");
    if (netCore.Success)
    {
        keys.Add($".NETCoreApp,Version=v{netCore.Groups[1].Value}");
    }

    return keys.ToArray();
}

public string PackageConsumptionNormalizePath(string path)
{
    return System.IO.Path.GetFullPath(path).TrimEnd('/', '\\');
}

public void PackageConsumptionAssertMatrixMatchesNuspecs(List<string> errors)
{
    foreach (var testCase in packageConsumptionMatrix)
    {
        foreach (var expected in testCase.ExpectedAssets)
        {
            var nuspecName = releasePackageSet.FirstOrDefault(e => e.Value == expected.PackageId).Key;
            if (nuspecName == null)
            {
                errors.Add($"{testCase.Name}: {expected.PackageId} is not in the lockstep package set (release.cake).");
                continue;
            }

            var nuspecText = System.IO.File.ReadAllText(ReleaseNuspecDirectory().CombineWithFilePath(nuspecName).FullPath);
            if (!Regex.IsMatch(nuspecText, $@"target=""lib[\\/]{Regex.Escape(expected.Lib)}"""))
            {
                errors.Add(
                    $"{testCase.Name}: the matrix expects {expected.PackageId} lib/{expected.Lib}, which nuget/{nuspecName} " +
                    "does not declare. Update the matrix in cake-build/tasks/package-consumption.cake to the new layout.");
            }
        }
    }
}

public void PackageConsumptionAssertAssets(PackageConsumptionCase testCase, FilePath assetsFile, string releaseVersion, List<string> errors)
{
    using (var assets = JsonDocument.Parse(System.IO.File.ReadAllText(assetsFile.FullPath)))
    {
        var targets = assets.RootElement.GetProperty("targets");
        var targetKeys = PackageConsumptionTargetKeys(testCase.TargetFramework);
        var target = targets.EnumerateObject().FirstOrDefault(t => targetKeys.Contains(t.Name, StringComparer.OrdinalIgnoreCase)).Value;
        if (target.ValueKind != JsonValueKind.Object)
        {
            errors.Add(
                $"{testCase.Name}: {assetsFile.FullPath} has no target for {testCase.TargetFramework} (looked for " +
                $"{string.Join(", ", targetKeys)}; found {string.Join(", ", targets.EnumerateObject().Select(t => t.Name))}).");
            return;
        }

        foreach (var expected in testCase.ExpectedAssets)
        {
            var library = target.EnumerateObject()
                .FirstOrDefault(p => p.Name.StartsWith(expected.PackageId + "/", StringComparison.OrdinalIgnoreCase));
            if (library.Value.ValueKind == JsonValueKind.Undefined)
            {
                errors.Add($"{testCase.Name}: {expected.PackageId} was not resolved at all.");
                continue;
            }

            var resolvedVersion = library.Name.Substring(expected.PackageId.Length + 1);
            if (resolvedVersion != releaseVersion)
            {
                errors.Add($"{testCase.Name}: resolved {expected.PackageId} {resolvedVersion}, expected {releaseVersion}.");
            }

            foreach (var group in new[] { "compile", "runtime" })
            {
                var selected = library.Value.TryGetProperty(group, out var files)
                    ? files.EnumerateObject().Select(f => f.Name).ToList()
                    : new List<string>();
                var expectedAssembly = $"lib/{expected.Lib}/{expected.PackageId}.dll";

                if (!selected.Contains(expectedAssembly, StringComparer.OrdinalIgnoreCase) ||
                    selected.Any(f => !f.StartsWith($"lib/{expected.Lib}/", StringComparison.OrdinalIgnoreCase)))
                {
                    errors.Add(
                        $"{testCase.Name}: NuGet selected {group} assets [{string.Join(", ", selected)}] from " +
                        $"{expected.PackageId}, expected {expectedAssembly}. Either the package layout changed (update " +
                        "the matrix in cake-build/tasks/package-consumption.cake) or a lib/<tfm> asset went missing.");
                }
            }
        }
    }
}

public void PackageConsumptionAssertLocalSource(DirectoryPath globalPackages, DirectoryPath packageSource, string releaseVersion, List<string> errors)
{
    foreach (var packageId in releasePackageSet.Values.OrderBy(v => v))
    {
        var metadata = globalPackages.Combine(packageId.ToLowerInvariant()).Combine(releaseVersion.ToLowerInvariant()).CombineWithFilePath(".nupkg.metadata");
        if (!FileExists(metadata))
        {
            errors.Add($"{packageId} {releaseVersion} was not restored into {globalPackages.FullPath}.");
            continue;
        }

        using (var json = JsonDocument.Parse(System.IO.File.ReadAllText(metadata.FullPath)))
        {
            var source = json.RootElement.TryGetProperty("source", out var s) ? s.GetString() : null;
            if (source == null || !string.Equals(PackageConsumptionNormalizePath(source), PackageConsumptionNormalizePath(packageSource.FullPath), StringComparison.OrdinalIgnoreCase))
            {
                errors.Add($"{packageId} {releaseVersion} was restored from '{source}', not from the packed packages in {packageSource.FullPath}.");
            }
        }
    }
}

Task("_Test_PackageConsumption")
    .Description("Restore, build and run the package-consumption apps against already packed .nupkg files")
    .Does(() =>
{
    var releaseVersion = ReleaseReadRequiredVersionArgument();
    var packageSource = MakeAbsolute(ReleasePackageOutputDirectory());
    var appsSource = paths.Root.Combine("tests/PackageConsumption");
    var workRoot = paths.TestResults.Combine("package-consumption");
    var appsRoot = workRoot.Combine("apps");
    var globalPackages = workRoot.Combine("packages");
    var logs = workRoot.Combine("logs");

    Information($"Package consumption test for {releaseVersion}, packages from {packageSource.FullPath}");

    var errors = new List<string>();
    PackageConsumptionAssertMatrixMatchesNuspecs(errors);
    foreach (var packageId in releasePackageSet.Values)
    {
        var nupkg = packageSource.CombineWithFilePath($"{packageId}.{releaseVersion}.nupkg");
        if (!FileExists(nupkg))
        {
            errors.Add($"{nupkg.FullPath} does not exist. Pack first (Test.PackageConsumption does), or pass the --packageOutput the packages were written to.");
        }
    }

    if (errors.Count > 0)
    {
        throw new Exception("Package consumption test cannot start:\n\n  - " + string.Join("\n\n  - ", errors) + "\n");
    }

    FilePath mono = null;
    if (!IsRunningOnWindows() && packageConsumptionMatrix.Any(c => c.IsNetFramework))
    {
        mono = Context.Tools.Resolve("mono");
        if (mono == null)
        {
            throw new Exception("The matrix has .NET Framework apps, and outside Windows they run on Mono, which is not on the PATH.");
        }
    }

    // A fresh copy every run: no bin/obj or restore state from a previous version survives.
    if (DirectoryExists(workRoot))
    {
        DeleteDirectory(workRoot, new DeleteDirectorySettings { Recursive = true, Force = true });
    }

    CopyDirectory(appsSource, appsRoot);
    foreach (var stale in GetDirectories(appsRoot.FullPath + "/*/bin").Concat(GetDirectories(appsRoot.FullPath + "/*/obj")))
    {
        DeleteDirectory(stale, new DeleteDirectorySettings { Recursive = true, Force = true });
    }

    CreateDirectory(globalPackages);
    CreateDirectory(logs);

    var nugetConfig = appsRoot.CombineWithFilePath("nuget.config");
    System.IO.File.WriteAllText(nugetConfig.FullPath, string.Join("\n", new[]
    {
        "<?xml version=\"1.0\" encoding=\"utf-8\"?>",
        "<configuration>",
        "  <config>",
        $"    <add key=\"globalPackagesFolder\" value=\"{globalPackages.FullPath}\" />",
        "  </config>",
        "  <packageSources>",
        "    <clear />",
        $"    <add key=\"local\" value=\"{packageSource.FullPath}\" />",
        "    <add key=\"nuget.org\" value=\"https://api.nuget.org/v3/index.json\" />",
        "  </packageSources>",
        "  <packageSourceMapping>",
        "    <packageSource key=\"local\">",
        "      <package pattern=\"Ably.PubSub.*\" />",
        "    </packageSource>",
        "    <packageSource key=\"nuget.org\">",
        "      <package pattern=\"*\" />",
        "    </packageSource>",
        "  </packageSourceMapping>",
        "</configuration>",
        string.Empty
    }));

    var results = new List<string>();

    foreach (var app in packageConsumptionMatrix.GroupBy(c => c.App))
    {
        var appDirectory = appsRoot.Combine(app.Key);
        var project = appDirectory.CombineWithFilePath($"{app.Key}.csproj");
        var targetFrameworks = string.Join(";", app.Select(c => c.TargetFramework));

        System.IO.File.WriteAllText(
            appDirectory.CombineWithFilePath("PackageConsumption.generated.props").FullPath,
            "<Project>\n" +
            "  <PropertyGroup>\n" +
            $"    <AblyPackageVersion>{releaseVersion}</AblyPackageVersion>\n" +
            $"    <PackageConsumptionTargetFrameworks>{targetFrameworks}</PackageConsumptionTargetFrameworks>\n" +
            "  </PropertyGroup>\n" +
            "</Project>\n");

        Information($"Restoring and building {app.Key} for {targetFrameworks}...");
        DotNetRestore(project.FullPath, new DotNetRestoreSettings { ConfigFile = nugetConfig });
        DotNetBuild(project.FullPath, new DotNetBuildSettings { Configuration = "Release", NoRestore = true });

        var assetsFile = appDirectory.CombineWithFilePath("obj/project.assets.json");
        foreach (var testCase in app)
        {
            var assetErrors = new List<string>();
            PackageConsumptionAssertAssets(testCase, assetsFile, releaseVersion, assetErrors);
            errors.AddRange(assetErrors);

            var output = appDirectory.Combine($"bin/Release/{testCase.TargetFramework}");
            FilePath tool;
            string arguments;
            string runtime;
            if (testCase.IsNetFramework)
            {
                var exe = output.CombineWithFilePath($"{app.Key}.exe");
                tool = mono ?? exe;
                arguments = mono != null ? $"\"{exe.FullPath}\"" : string.Empty;
                runtime = mono != null ? "Mono" : ".NET Framework";
            }
            else
            {
                tool = Context.Tools.Resolve(IsRunningOnWindows() ? "dotnet.exe" : "dotnet") ?? new FilePath("dotnet");
                arguments = $"\"{output.CombineWithFilePath($"{app.Key}.dll").FullPath}\"";
                runtime = ".NET";
            }

            Information($"Running {testCase.Name} on {runtime}...");
            var exitCode = StartProcess(tool, new ProcessSettings
            {
                Arguments = arguments,
                WorkingDirectory = output,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                Timeout = 300000
            }, out var standardOutput, out var standardError);

            var log = standardOutput.Concat(standardError).ToList();
            System.IO.File.WriteAllLines(logs.CombineWithFilePath($"{app.Key}-{testCase.TargetFramework}.log").FullPath, log);
            foreach (var line in log)
            {
                Information($"    {line}");
            }

            if (exitCode != 0)
            {
                var firstException = log.FirstOrDefault(l => l.Contains("Exception"));
                errors.Add($"{testCase.Name} exited {exitCode} on {runtime}: {firstException ?? "see " + logs.FullPath}");
            }

            var assets = string.Join(" + ", testCase.ExpectedAssets.Select(a => $"{a.PackageId} lib/{a.Lib}"));
            var outcome = exitCode == 0 && assetErrors.Count == 0 ? "PASS" : "FAIL";
            results.Add($"{outcome}  {testCase.Name,-17}{assets} on {runtime}");
        }
    }

    PackageConsumptionAssertLocalSource(globalPackages, packageSource, releaseVersion, errors);

    Information("Package consumption results:");
    foreach (var result in results)
    {
        Information($"  {result}");
    }

    if (errors.Count > 0)
    {
        throw new Exception(
            $"Package consumption test failed with {errors.Count} problem(s); the packed packages do not work " +
            "the way a customer consumes them:\n\n  - " + string.Join("\n\n  - ", errors) + "\n");
    }

    Information($"All {packageConsumptionMatrix.Length} package-consumption cases passed against {releaseVersion}.");
});

///////////////////////////////////////////////////////////////////////////////
// PUBLIC TARGETS
///////////////////////////////////////////////////////////////////////////////

Task("Test.PackageConsumption")
    .Description("Pack all three packages, then build and run apps that consume them as NuGet packages")
    .IsDependentOn("Package")
    .IsDependentOn("_Test_PackageConsumption");

Task("Test.PackageConsumption.NoPack")
    .Description("Build and run the package-consumption apps against .nupkg files already in --packageOutput")
    .IsDependentOn("_Test_PackageConsumption");

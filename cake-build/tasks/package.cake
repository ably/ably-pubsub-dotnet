///////////////////////////////////////////////////////////////////////////////
// PACKAGE SET AND OUTPUT
///////////////////////////////////////////////////////////////////////////////

// The NuGet packages built from this repository: nuspec file name -> package id.
// All three ship together at one version, and each door package pins the core
// exactly. Packed core-first, the order they are published in (CONTRIBUTING.md).
var nugetPackageSet = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
{
    { "ably.pubsub.core.nuspec", "Ably.PubSub.Core" },
    { "ably.pubsub.device.nuspec", "Ably.PubSub.Device" },
    { "ably.pubsub.server.nuspec", "Ably.PubSub.Server" }
};

public DirectoryPath NuspecDirectory()
{
    return paths.Root.Combine("nuget");
}

// Where Package writes the .nupkg files and where Test.PackageConsumption.NoPack
// reads them: --packageOutput, or the repository root by default.
public DirectoryPath PackageOutputDirectory()
{
    if (string.IsNullOrEmpty(packageOutput))
    {
        return paths.Root;
    }

    // Resolved against the repository root, not Cake's working directory (which
    // is cake-build/), so --packageOutput=consumption-packages means what it reads like.
    DirectoryPath candidate = packageOutput;
    return candidate.IsRelative ? paths.Root.Combine(candidate) : candidate;
}

public string RequirePackageVersionArgument()
{
    if (string.IsNullOrWhiteSpace(version))
    {
        throw new Exception(
            "No --version was supplied. Pass the package version after a -- separator, e.g. " +
            "./build.sh -- --target=Package --version=2.0.0 (Cake reserves --version for itself).");
    }

    return version;
}

// Asserts, in the produced .nupkg files, what the source nuspecs cannot prove:
// that the packer substituted $version$ into each package's <version> and into
// every door dependency group's Ably.PubSub.Core dependency as the exact range
// [x.y.z]. A loose pin (x.y.z alone is a minimum) still restores against the
// local source the package-install test uses, so only this check catches it.
public List<string> VerifyPackedNuGetPackages(DirectoryPath packageDirectory, string packageVersion)
{
    var errors = new List<string>();
    var corePackageId = nugetPackageSet["ably.pubsub.core.nuspec"];
    var exactPin = $"[{packageVersion}]";

    foreach (var packageId in nugetPackageSet.Values)
    {
        var nupkg = packageDirectory.CombineWithFilePath($"{packageId}.{packageVersion}.nupkg");
        if (!FileExists(nupkg))
        {
            errors.Add($"{nupkg.FullPath} was not produced.");
            continue;
        }

        System.Xml.Linq.XDocument nuspec;
        using (var archive = System.IO.Compression.ZipFile.OpenRead(nupkg.FullPath))
        {
            var entry = archive.Entries.SingleOrDefault(e =>
                !e.FullName.Contains("/") && e.FullName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase));
            if (entry == null)
            {
                errors.Add($"{nupkg.GetFilename()} contains no .nuspec.");
                continue;
            }

            using (var stream = entry.Open())
            {
                nuspec = System.Xml.Linq.XDocument.Load(stream);
            }

            // Neither door ships a .NET Framework asset: a .NET Framework app gets the door's
            // netstandard2.0 asset with the core's net46 one, which Test.PackageConsumption runs.
            if (packageId != corePackageId)
            {
                errors.AddRange(archive.Entries
                    .Where(e => e.FullName.StartsWith("lib/net4", StringComparison.OrdinalIgnoreCase))
                    .Select(e => $"{nupkg.GetFilename()} contains {e.FullName}; the door packages carry no .NET Framework lib asset."));
            }
        }

        var metadata = nuspec.Root.Elements().Single(e => e.Name.LocalName == "metadata");
        var packedVersion = metadata.Elements().FirstOrDefault(e => e.Name.LocalName == "version")?.Value;
        if (packedVersion != packageVersion)
        {
            errors.Add($"{nupkg.GetFilename()} declares version '{packedVersion}', expected '{packageVersion}'.");
        }

        if (packageId == corePackageId)
        {
            continue;
        }

        var groups = metadata.Descendants().Where(e => e.Name.LocalName == "group").ToList();
        if (groups.Count == 0)
        {
            errors.Add($"{nupkg.GetFilename()} has no dependency groups, so it does not pin {corePackageId}.");
        }

        foreach (var group in groups)
        {
            var targetFramework = (string)group.Attribute("targetFramework") ?? "(any)";
            if (targetFramework.StartsWith(".NETFramework", StringComparison.OrdinalIgnoreCase))
            {
                errors.Add($"{nupkg.GetFilename()} has a {targetFramework} dependency group; the door packages have no .NET Framework asset.");
            }

            var pins = group.Elements()
                .Where(e => e.Name.LocalName == "dependency" &&
                            string.Equals((string)e.Attribute("id"), corePackageId, StringComparison.OrdinalIgnoreCase))
                .Select(e => (string)e.Attribute("version"))
                .ToList();

            if (pins.Count != 1 || pins[0] != exactPin)
            {
                errors.Add(
                    $"{nupkg.GetFilename()} group {targetFramework} pins {corePackageId} as " +
                    $"[{string.Join(", ", pins.Select(p => $"'{p}'"))}], expected exactly '{exactPin}'. Without the " +
                    "brackets the pin is a minimum, and a consumer could resolve a core this door was not built with.");
            }
            else
            {
                Information($"  {packageId} {targetFramework}: {corePackageId} {pins[0]}");
            }
        }
    }

    return errors;
}

///////////////////////////////////////////////////////////////////////////////
// PACKAGE TASKS (Internal)
///////////////////////////////////////////////////////////////////////////////

Task("_Package_Require_Version")
    .Does(() =>
{
    // Without a version, _Package_Create_NuGet would be skipped and Package would
    // succeed having packed nothing.
    RequirePackageVersionArgument();
});

Task("_Restore_Package")
    .Does(() =>
{
    RestoreSolution(paths.PackageSolution);
});

Task("_Package_Build_All")
    .IsDependentOn("_Package_Require_Version")
    .IsDependentOn("_Clean")
    .IsDependentOn("_Version")
    .IsDependentOn("_Restore_Package")
    .Does(() =>
{
    Information("Building all projects for packaging...");
    
    var settings = buildConfig.ApplyStandardSettings(
        new MSBuildSettings(),
        "Release"
    );
    
    settings = buildConfig.ApplyPackageSettings(settings);
    settings = settings.WithTarget("Build");

    MSBuild(paths.PackageSolution, settings);

    // On macOS/Linux the MSBuild above is Mono's, and under Mono the SDK-style heads build only
    // netstandard2.0 (their csproj narrows TargetFrameworks when MSBuildRuntimeType is Mono). Build
    // their remaining target frameworks with the .NET SDK, with the same Package properties, so a
    // Unix pack carries every lib/<tfm> the nuspecs declare. The door projects build the core
    // through their project references. Windows MSBuild already builds every target framework.
    if (!IsRunningOnWindows())
    {
        var sdkPackageHeads = new[]
        {
            paths.Src.CombineWithFilePath("Ably.PubSub.Device/Ably.PubSub.Device.csproj"),
            paths.Src.CombineWithFilePath("Ably.PubSub.Server/Ably.PubSub.Server.csproj")
        };

        foreach (var project in sdkPackageHeads)
        {
            Information($"Building every target framework of {project.GetFilename()} with the .NET SDK...");
            DotNetBuild(project.FullPath, new DotNetBuildSettings
            {
                Configuration = "Release",
                NoRestore = true,
                MSBuildSettings = new DotNetMSBuildSettings()
                    .WithProperty("StyleCopEnabled", "true")
                    .WithProperty("Package", "true")
                    .WithProperty("DefineConstants", "PACKAGE")
                    .WithProperty("Deterministic", "true")
                    .WithProperty("ContinuousIntegrationBuild", BuildSystem.IsLocalBuild ? "false" : "true")
                    .WithProperty("GenerateDocumentationFile", "true")
            });
        }
    }
});

// The signed net46 core must not reference the unsigned IO.Ably.DeltaCodec, so merge it in
// (internalized, signed with Ably.PubSub.snk), as ably.io 1.2.16+ did. Only DeltaCodec is merged.
Task("_Package_Merge_NetFramework")
    .IsDependentOn("_Package_Build_All")
    .Does(() =>
{
    var binPath = paths.Src.Combine("Ably.PubSub.Core.NETFramework/bin/Release");
    var packagedPath = binPath.Combine("Packaged");

    ilRepackHelper.MergeDLLs(
        binPath.CombineWithFilePath("Ably.PubSub.Core.dll"),
        new[] { binPath.CombineWithFilePath("IO.Ably.DeltaCodec.dll") },
        packagedPath.CombineWithFilePath("Ably.PubSub.Core.dll"));

    CopyFile(binPath.CombineWithFilePath("Ably.PubSub.Core.xml"), packagedPath.CombineWithFilePath("Ably.PubSub.Core.xml"));
});

Task("_Package_Create_NuGet")
    .IsDependentOn("_Package_Merge_NetFramework")
    .WithCriteria(() => !string.IsNullOrEmpty(version))
    .Does(() =>
{
    Information($"Creating NuGet packages version {version}...");

    var nugetSettings = new NuGetPackSettings
    {
        Version = version,
        Properties = new Dictionary<string, string>
        {
            { "Configuration", "Release" }
        },
        OutputDirectory = PackageOutputDirectory()
    };

    CreateDirectory(nugetSettings.OutputDirectory);

    // Use local nuget.exe if available
    var nugetPath = paths.Root.CombineWithFilePath("tools/nuget.exe");
    if (FileExists(nugetPath))
    {
        nugetSettings.ToolPath = nugetPath;
    }

    foreach (var nuspec in nugetPackageSet.Keys)
    {
        var nuspecFile = NuspecDirectory().CombineWithFilePath(nuspec);

        if (!FileExists(nuspecFile))
        {
            throw new Exception($"Nuspec file not found: {nuspecFile}");
        }

        NuGetPack(nuspecFile, nugetSettings);

        Information($"✓ Packed {nuspecFile.GetFilename()} at version {version}");
    }
});

Task("_Package_Verify_NuGet")
    .Description("Assert the packed packages carry the version and each door pins the core exactly")
    .Does(() =>
{
    var packageVersion = RequirePackageVersionArgument();
    var packageDirectory = PackageOutputDirectory();

    Information($"Verifying the packed packages in {packageDirectory.FullPath}");
    var errors = VerifyPackedNuGetPackages(packageDirectory, packageVersion);

    if (errors.Count > 0)
    {
        throw new Exception(
            $"Packed package verification failed with {errors.Count} problem(s); do not publish these " +
            "packages:\n\n  - " + string.Join("\n\n  - ", errors) + "\n");
    }

    Information($"All {nugetPackageSet.Count} packages are at {packageVersion}, and every door dependency group pins the core as [{packageVersion}].");
});

Task("_Package_Unity")
    .WithCriteria(() => !string.IsNullOrEmpty(version))
    .Does(() =>
{
    var committedPlugin = paths.UnityPluginDll;
    if (!FileExists(committedPlugin))
    {
        throw new Exception(
            $"Committed Unity plugin missing: {committedPlugin}. The .unitypackage bundles this merged " +
            "assembly; regenerate it with ./unity-plugins-updater.sh <version> and commit it.");
    }

    Information($"Creating Unity package version {version}...");
    
    var unityPackagerPath = paths.Root.Combine("unity-packager");
    var outputPath = paths.Root.CombineWithFilePath($"ably.pubsub.{version}.unitypackage");
    
    // Clone unity-packager if not exists
    if (!DirectoryExists(unityPackagerPath))
    {
        Information("Cloning unity-packager repository...");
        StartProcess("git", new ProcessSettings
        {
            Arguments = $"clone https://github.com/ably-forks/unity-packager.git -b v1.0.0 \"{unityPackagerPath.FullPath}\"",
            WorkingDirectory = paths.Root
        });
    }
    
    var unityPackagerProject = unityPackagerPath.CombineWithFilePath("UnityPackageExporter/UnityPackageExporter.csproj");
    
    if (!FileExists(unityPackagerProject))
    {
        Warning("Unity packager project not found, skipping Unity package creation");
        return;
    }
    
    Information("Building Unity package...");
    StartProcess("dotnet", new ProcessSettings
    {
        Arguments = $"run --project \"{unityPackagerProject.FullPath}\" " +
                   $"-project \"{paths.Root.Combine("unity").FullPath}\" -output \"{outputPath.FullPath}\" -dir Assets/Ably",
        WorkingDirectory = paths.Root
    });
    
    if (FileExists(outputPath))
    {
        Information($"✓ Unity package created: {outputPath}");
    }
    else
    {
        Warning("Unity package was not created");
    }
});

///////////////////////////////////////////////////////////////////////////////
// PUBLIC TARGETS
///////////////////////////////////////////////////////////////////////////////

Task("Package")
    .Description("Create the NuGet packages (Ably.PubSub.Core, Ably.PubSub.Device, Ably.PubSub.Server)")
    // Dependencies run in declaration order: pack, then check the packed pins.
    .IsDependentOn("_Package_Create_NuGet")
    .IsDependentOn("_Package_Verify_NuGet");

Task("Package.Verify")
    .Description("Check .nupkg files already in --packageOutput: version, and each door pins the core exactly")
    .IsDependentOn("_Package_Verify_NuGet");

Task("UnityPackage")
    .Description("Create Unity package")
    .IsDependentOn("_Package_Unity");

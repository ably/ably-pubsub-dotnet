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

Task("_Package_Create_NuGet")
    .IsDependentOn("_Package_Build_All")
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
    .IsDependentOn("_Package_Create_NuGet");

Task("UnityPackage")
    .Description("Create Unity package")
    .IsDependentOn("_Package_Unity");

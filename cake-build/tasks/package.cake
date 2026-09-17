///////////////////////////////////////////////////////////////////////////////
// PACKAGE TASKS (Internal)
///////////////////////////////////////////////////////////////////////////////

Task("_Restore_Package")
    .Does(() =>
{
    RestoreSolution(paths.PackageSolution);
});

Task("_Package_Build_All")
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
});

Task("_Package_Create_NuGet")
    .IsDependentOn("_Package_Build_All")
    .WithCriteria(() => !string.IsNullOrEmpty(version))
    .Does(() =>
{
    Information($"Creating NuGet packages version {version}...");

    // The lockstep package set. Every package here is built from this repository
    // and released at the same version. Stack PR 2 adds the door packages
    // (ably.pubsub.device.nuspec, ably.pubsub.server.nuspec) to this list.
    var nuspecFiles = new[]
    {
        "nuget/ably.pubsub.core.nuspec"
    };

    var nugetSettings = new NuGetPackSettings
    {
        Version = version,
        Properties = new Dictionary<string, string>
        {
            { "Configuration", "Release" }
        },
        OutputDirectory = paths.Root
    };

    // Use local nuget.exe if available
    var nugetPath = paths.Root.CombineWithFilePath("tools/nuget.exe");
    if (FileExists(nugetPath))
    {
        nugetSettings.ToolPath = nugetPath;
    }

    foreach (var nuspec in nuspecFiles)
    {
        var nuspecFile = paths.Root.CombineWithFilePath(nuspec);

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
    var committedPlugin = paths.Root.CombineWithFilePath("unity/Assets/Ably/Plugins/Ably.PubSub.Device.dll");
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
    .Description("Create the NuGet packages (Ably.PubSub.Core)")
    .IsDependentOn("_Package_Create_NuGet");

Task("UnityPackage")
    .Description("Create Unity package")
    .IsDependentOn("_Package_Unity");

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
                    .WithProperty("GenerateDocumentationFile", "true")
            });
        }
    }
});

Task("_Package_Create_NuGet")
    .IsDependentOn("_Package_Build_All")
    // Runs between the build and the pack: nuget silently omits a files entry it
    // cannot resolve, so this is the last moment an empty package can be caught.
    .IsDependentOn("_Release_Verify_Files")
    .WithCriteria(() => !string.IsNullOrEmpty(version))
    .Does(() =>
{
    Information($"Creating NuGet packages version {version}...");

    // The lockstep package set. Every package here is built from this repository
    // and released at the same version, and both door packages pin the core exactly.
    // Packed core-first so the order matches the publish order stack PR 3 adds.
    var nuspecFiles = new[]
    {
        "nuget/ably.pubsub.core.nuspec",
        "nuget/ably.pubsub.device.nuspec",
        "nuget/ably.pubsub.server.nuspec"
    };

    var nugetSettings = new NuGetPackSettings
    {
        Version = version,
        Properties = new Dictionary<string, string>
        {
            { "Configuration", "Release" }
        },
        OutputDirectory = ReleasePackageOutputDirectory()
    };

    CreateDirectory(nugetSettings.OutputDirectory);

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
    // Dependencies run in declaration order: pack, then assert the produced
    // .nupkg files. Together with _Release_Preflight (before the build) and
    // _Release_Verify_Files (before the pack), the pack is bracketed by checks.
    .IsDependentOn("_Package_Create_NuGet")
    .IsDependentOn("_Release_Verify_Packages");

Task("UnityPackage")
    .Description("Create Unity package")
    .IsDependentOn("_Package_Unity");

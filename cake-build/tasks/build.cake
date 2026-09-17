///////////////////////////////////////////////////////////////////////////////
// BUILD TASKS (Internal)
///////////////////////////////////////////////////////////////////////////////

Task("_Clean")
    .Does(() =>
{
    Information("Cleaning build directories...");
    
    // Clean the main solution which includes all core projects (NetFramework, NetStandard, iOS, Android, Tests, etc.)
    CleanSolution();
    
    // Clean custom output directories that are not part of standard project outputs
    CleanDirectory(paths.TestResults);    
});

Task("_Restore_Main")
    .Does(() =>
{
    RestoreSolution(paths.MainSolution);
});

Task("_Version")
    .WithCriteria(() => !string.IsNullOrEmpty(version))
    .Does(() =>
{
    Information($"Setting version to {version}");
    
    var assemblyInfoPath = paths.Src.CombineWithFilePath("CommonAssemblyInfo.cs");
    
    CreateAssemblyInfo(assemblyInfoPath, new AssemblyInfoSettings
    {
        Company = "Ably",
        Product = "Ably .NET Library",
        Copyright = $"Copyright © Ably {DateTime.Now.Year}",
        Version = version,
        FileVersion = version,
        InformationalVersion = version
    });
});

Task("_NetFramework_Build")
    .Does(() =>
{
    Information("Building .NET Framework solution...");
    
    var settings = buildConfig.ApplyStandardSettings(
        new MSBuildSettings(),
        configuration
    );
    
    settings = settings.WithTarget("Build");
    
    MSBuild(paths.NetFrameworkSolution, settings);
});

Task("_NetStandard_Build")
    .Does(() =>
{
    Information("Building .NET Standard solution...");
    
    var settings = new DotNetBuildSettings
    {
        Configuration = configuration,
        NoRestore = true
    };
    var msbuildSettings = new DotNetMSBuildSettings();
    
    
    if (!string.IsNullOrEmpty(defineConstants))
    {
        msbuildSettings = msbuildSettings.WithProperty("DefineConstants", defineConstants);
    }
    
    settings.MSBuildSettings = msbuildSettings;
    
    DotNetBuild(paths.NetStandardSolution.FullPath, settings);
});

Task("_Build_Ably_Unity_Dll")
    .Description("Create merged Unity DLL with all dependencies")
    .Does(() =>
{
    Information("Merging Unity dependencies into Ably.PubSub.Core.dll...");
    
    var netStandard20BinPath = paths.Src
        .Combine("Ably.PubSub.Core")
        .Combine("bin/Release/netstandard2.0");
    
    if (!DirectoryExists(netStandard20BinPath))
    {
        throw new Exception($"NETStandard2.0 bin directory not found: {netStandard20BinPath}. Please build the project first.");
    }
    
    var primaryDll = netStandard20BinPath.CombineWithFilePath("Ably.PubSub.Core.dll");
    
    if (!FileExists(primaryDll))
    {
        throw new Exception($"Primary DLL not found: {primaryDll}. Please build the Ably.PubSub.Core project first.");
    }
    
    var newtonsoftDll = paths.Root
        .Combine("lib/unity/AOT")
        .CombineWithFilePath("Newtonsoft.Json.dll");
    
    if (!FileExists(newtonsoftDll))
    {
        throw new Exception($"Newtonsoft.Json.dll not found at: {newtonsoftDll}");
    }
    
    var dllsToMerge = new[]
    {
        netStandard20BinPath.CombineWithFilePath("IO.Ably.DeltaCodec.dll"),
        netStandard20BinPath.CombineWithFilePath("System.Runtime.CompilerServices.Unsafe.dll"),
        netStandard20BinPath.CombineWithFilePath("System.Threading.Channels.dll"),
        netStandard20BinPath.CombineWithFilePath("System.Threading.Tasks.Extensions.dll"),
        newtonsoftDll
    };
    
    var unityOutputPath = paths.Root.Combine("unity/Assets/Ably/Plugins");
    var outputDll = unityOutputPath.CombineWithFilePath("Ably.PubSub.Core.dll");
    
    // Delete existing output DLL if it exists
    if (FileExists(outputDll))
    {
        DeleteFile(outputDll);
        Information($"Deleted existing DLL: {outputDll}");
    }
    
    // Merge all dependencies into primary DLL in one go
    ilRepackHelper.MergeDLLs(primaryDll, dllsToMerge, outputDll);
    
    Information($"✓ Unity DLL created at: {outputDll}");
});

///////////////////////////////////////////////////////////////////////////////
// PUBLIC TARGETS
///////////////////////////////////////////////////////////////////////////////
// These are the tasks that should be called directly by users or CI/CD

// Public task: Build .NET Framework projects
Task("Build.NetFramework")
    .Description("Build .NET Framework solution")
    .IsDependentOn("_Clean")
    .IsDependentOn("_Restore_Main")
    .IsDependentOn("_NetFramework_Build");

// Public task: Build .NET Standard projects
Task("Build.NetStandard")
    .Description("Build .NET Standard solution")
    .IsDependentOn("_Clean")
    .IsDependentOn("_Restore_Main")
    .IsDependentOn("_NetStandard_Build");

// Public task: Update Ably DLLs inside unity project
Task("Update.AblyUnity")
    .Description("Update Ably DLLs inside unity project")
    .IsDependentOn("_Build_Ably_Unity_Dll");

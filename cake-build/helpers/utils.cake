///////////////////////////////////////////////////////////////////////////////
// UTILITY HELPERS
///////////////////////////////////////////////////////////////////////////////

/// <summary>
/// Restores NuGet packages for a solution using both NuGet restore and dotnet restore.
/// This handles both legacy packages.config projects and modern SDK-style projects.
/// </summary>
/// <param name="solutionPath">Path to the solution file to restore</param>
public void RestoreSolution(FilePath solutionPath)
{
    if (!FileExists(solutionPath))
    {
        throw new Exception($"Solution file not found: {solutionPath.FullPath}");
    }

    Information($"Restoring NuGet packages for {solutionPath.GetFilename()}...");

    // This is needed to restore deprecated Xamarin projects on Windows and macOS/Linux.
    // Needed for projects using old packages.config format for maintaining dependencies.
    // This will not be needed once deprecated projects are removed.
    Information("Running NuGet restore...");
    if (IsRunningOnWindows())
    {
        Information("Windows system detected, running direct NuGetRestore command");
        // Throws on failure, which is what we want: a failed packages.config
        // restore must stop the build here, not resurface later as a confusing
        // EnsureNuGetPackageBuildImports error.
        NuGetRestore(solutionPath.FullPath, new NuGetRestoreSettings
        {
            Verbosity = NuGetVerbosity.Quiet
        });
    }
    else
    {
        // On macOS/Linux the `nuget` CLI exists only where Mono tooling was installed
        // (the mono workflow runs ./tools/mono-install.sh; the plain macOS/Linux test
        // legs do not, and macos-14/ubuntu-24.04 runners ship no Mono). The legacy
        // packages.config heads it restores are only *built* on Windows/Mono anyway,
        // so a missing tool is skippable — but a present tool that fails is a real
        // error. StartProcess returns the exit code rather than throwing, so check it.
        var nugetTool = Context.Tools.Resolve("nuget") ?? Context.Tools.Resolve("nuget.exe");
        if (nugetTool == null)
        {
            Warning("nuget CLI not found; skipping packages.config restore (only needed for the net46 heads, which build on Windows/Mono).");
        }
        else
        {
            Information("macOS/Linux system detected, running nuget restore from CLI");
            var nugetExit = StartProcess(nugetTool, new ProcessSettings
            {
                Arguments = $"restore \"{solutionPath.FullPath}\" -Verbosity quiet"
            });
            if (nugetExit != 0)
            {
                throw new Exception(
                    $"nuget restore failed for {solutionPath.GetFilename()} with exit code {nugetExit}.");
            }
        }
    }

    // dotnet restore (all platforms, for SDK-style projects). No try/catch: a
    // restore failure must fail the build at the point it happens.
    Information("Running dotnet restore...");
    // Suppress restore warning as errors NU1503 for xamarin/old style projects
    var restoreSettings = new DotNetRestoreSettings
    {
        MSBuildSettings = new DotNetMSBuildSettings()
            .WithProperty("WarningsNotAsErrors", "NU1503")
            .WithProperty("NoWarn", "NU1503")
    };
    DotNetRestore(solutionPath.FullPath, restoreSettings);
    Information($"✓ dotnet restore completed");
}

/// <summary>
/// Cleans build outputs for all projects by deleting bin and obj directories.
/// Searches for all .csproj files in the src directory and cleans each one.
/// </summary>
public void CleanSolution()
{
    Information("Cleaning all build outputs...");
    
    // Get all project files in the main src directory
    var projectFiles = GetFiles($"{paths.Src}/**/*.csproj")
        .Where(f => !f.FullPath.Contains("node_modules") &&
                    !f.FullPath.Contains(".git") &&
                    !f.FullPath.Contains("packages"))
        .ToList();
    
    Information($"Found {projectFiles.Count} project(s) to clean");
    
    foreach (var projectFile in projectFiles)
    {
        Information($"Cleaning project: {projectFile.GetFilename()}");
        
        var projectDir = projectFile.GetDirectory();
        
        // Clean bin directory
        var binDir = projectDir.Combine("bin");
        if (DirectoryExists(binDir))
        {
            try
            {
                DeleteDirectory(binDir, new DeleteDirectorySettings {
                    Recursive = true,
                    Force = true
                });
                Information($"  ✓ Cleaned {binDir}");
            }
            catch (Exception ex)
            {
                Warning($"  Failed to clean {binDir}: {ex.Message}");
            }
        }
        
        // Clean obj directory
        var objDir = projectDir.Combine("obj");
        if (DirectoryExists(objDir))
        {
            try
            {
                DeleteDirectory(objDir, new DeleteDirectorySettings {
                    Recursive = true,
                    Force = true
                });
                Information($"  ✓ Cleaned {objDir}");
            }
            catch (Exception ex)
            {
                Warning($"  Failed to clean {objDir}: {ex.Message}");
            }
        }
    }
    
    Information($"✓ Completed cleaning all projects");
}

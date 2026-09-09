#load "helpers/paths.cake"
#load "helpers/utils.cake"
#load "helpers/tools.cake"
#load "helpers/test-retry.cake"
#load "helpers/build-config.cake"
#load "helpers/frameworks.cake"
#load "helpers/test-execution.cake"
#load "tasks/build.cake"
#load "tasks/test.cake"
#load "tasks/package.cake"

///////////////////////////////////////////////////////////////////////////////
// ARGUMENTS
///////////////////////////////////////////////////////////////////////////////

var target = Argument("target", "Build.NetStandard");
var configuration = Argument("configuration", "Release");
var version = Argument("version", "");
var defineConstants = Argument("define", "");
var framework = Argument("framework", "");

///////////////////////////////////////////////////////////////////////////////
// SETUP / TEARDOWN
///////////////////////////////////////////////////////////////////////////////

Setup(ctx =>
{
    Information("========================================");
    Information("Cake Build System for ably-pubsub-dotnet");
    Information("========================================");
    Information($"Target: {target}");
    Information($"Configuration: {configuration}");
    if (!string.IsNullOrEmpty(version))
        Information($"Version: {version}");
    if (!string.IsNullOrEmpty(framework))
    {
        Information($"Framework: {framework}");
        // Validate framework availability
        if (!frameworkDetector.IsFrameworkAvailable(framework))
        {
            var available = string.Join(", ", frameworkDetector.GetTargetFrameworks());
            Warning($"Framework '{framework}' may not be available on this system.");
            Information($"Available frameworks: {available}");
        }
    }
    else
    {
        // Show available frameworks when none specified
        var available = string.Join(", ", frameworkDetector.GetTargetFrameworks());
        Information($"Available frameworks: {available}");
    }
    Information($"Platform: {(IsRunningOnWindows() ? "Windows" : IsRunningOnUnix() ? "Unix" : "macOS")}");
});

Teardown(ctx =>
{
    Information("========================================");
    Information("Build completed");
    Information("========================================");
});

///////////////////////////////////////////////////////////////////////////////
// TASK EXECUTION
///////////////////////////////////////////////////////////////////////////////

RunTarget(target);

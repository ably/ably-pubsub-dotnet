///////////////////////////////////////////////////////////////////////////////
// TEST TASKS - NETFRAMEWORK (Internal)
///////////////////////////////////////////////////////////////////////////////

Task("_NetFramework_Unit_Tests")
    .IsDependentOn("Build.NetFramework")
    .Does(() =>
{
    Information("Running .NET Framework unit tests...");
    
    var testAssemblies = testExecutionHelper.FindTestAssemblies("Ably.PubSub.Tests.NETFramework");
    if (!testAssemblies.Any()) return;
    
    var settings = testExecutionHelper.CreateXUnitSettings("xunit-netframework-unit", isIntegration: false);
    testExecutionHelper.RunXUnitTests(testAssemblies, settings);
});

Task("_NetFramework_Unit_Tests_WithRetry")
    .IsDependentOn("Build.NetFramework")
    .Does(() =>
{
    Information("Running .NET Framework unit tests with retry...");
    
    var testAssemblies = testExecutionHelper.FindTestAssemblies("Ably.PubSub.Tests.NETFramework");
    if (!testAssemblies.Any()) return;
    
    var resultsPath = paths.TestResults.CombineWithFilePath("xunit-netframework-unit.xml");
    
    try
    {
        var settings = testExecutionHelper.CreateXUnitSettings("xunit-netframework-unit", isIntegration: false);
        testExecutionHelper.RunXUnitTests(testAssemblies, settings);
    }
    catch
    {
        Warning("Some tests failed. Retrying failed tests...");
    }
    
    testExecutionHelper.RetryFailedXUnitTests(
        testAssemblies, 
        resultsPath,
        testRetryHelper,
        (test) => testExecutionHelper.CreateXUnitSettings("retry", isIntegration: false, isRetry: true)
    );
});

Task("_NetFramework_Integration_Tests")
    .IsDependentOn("Build.NetFramework")
    .Does(() =>
{
    Information("Running .NET Framework integration tests...");
    
    var testAssemblies = testExecutionHelper.FindTestAssemblies("Ably.PubSub.Tests.NETFramework");
    if (!testAssemblies.Any()) return;
    
    var settings = testExecutionHelper.CreateXUnitSettings("xunit-netframework-integration", isIntegration: true);
    testExecutionHelper.RunXUnitTests(testAssemblies, settings);
});

Task("_NetFramework_Integration_Tests_WithRetry")
    .IsDependentOn("Build.NetFramework")
    .Does(() =>
{
    Information("Running .NET Framework integration tests with retry...");
    
    var testAssemblies = testExecutionHelper.FindTestAssemblies("Ably.PubSub.Tests.NETFramework");
    if (!testAssemblies.Any()) return;
    
    var resultsPath = paths.TestResults.CombineWithFilePath("xunit-netframework-integration.xml");
    
    try
    {
        var settings = testExecutionHelper.CreateXUnitSettings("xunit-netframework-integration", isIntegration: true);
        testExecutionHelper.RunXUnitTests(testAssemblies, settings);
    }
    catch
    {
        Warning("Some tests failed. Retrying failed tests...");
    }
    
    testExecutionHelper.RetryFailedXUnitTests(
        testAssemblies, 
        resultsPath,
        testRetryHelper,
        (test) => testExecutionHelper.CreateXUnitSettings("retry", isIntegration: true, isRetry: true)
    );
});

///////////////////////////////////////////////////////////////////////////////
// TEST TASKS - NETSTANDARD (Internal)
///////////////////////////////////////////////////////////////////////////////

Task("_NetStandard_Unit_Tests")
    .IsDependentOn("Build.NetStandard")
    .Does(() =>
{
    Information("Running .NET Standard unit tests...");
    
    var project = paths.Src.CombineWithFilePath("Ably.PubSub.Tests.DotNET/Ably.PubSub.Tests.DotNET.csproj");
    var resultsPath = paths.TestResults.CombineWithFilePath("tests-netstandard-unit.trx");
    
    var filter = testExecutionHelper.CreateUnitTestFilter(IsRunningOnUnix());
    var settings = testExecutionHelper.CreateDotNetTestSettings(resultsPath, filter, framework, configuration);
    
    testExecutionHelper.RunDotNetTests(project, settings);
});

Task("_NetStandard_Unit_Tests_WithRetry")
    .IsDependentOn("Build.NetStandard")
    .Does(() =>
{
    Information("Running .NET Standard unit tests with retry...");
    
    var project = paths.Src.CombineWithFilePath("Ably.PubSub.Tests.DotNET/Ably.PubSub.Tests.DotNET.csproj");
    var resultsPath = paths.TestResults.CombineWithFilePath("tests-netstandard-unit.trx");
    
    var filter = testExecutionHelper.CreateUnitTestFilter(IsRunningOnUnix());
    var settings = testExecutionHelper.CreateDotNetTestSettings(resultsPath, filter, framework, configuration);
    
    try
    {
        testExecutionHelper.RunDotNetTests(project, settings);
    }
    catch
    {
        Warning("Some tests failed. Retrying failed tests...");
    }
    
    testExecutionHelper.RetryFailedDotNetTests(project, resultsPath, testRetryHelper, framework, configuration);
});

Task("_NetStandard_Integration_Tests")
    .IsDependentOn("Build.NetStandard")
    .Does(() =>
{
    Information("Running .NET Standard integration tests...");
    
    var project = paths.Src.CombineWithFilePath("Ably.PubSub.Tests.DotNET/Ably.PubSub.Tests.DotNET.csproj");
    var resultsPath = paths.TestResults.CombineWithFilePath("tests-netstandard-integration.trx");
    
    var filter = testExecutionHelper.CreateIntegrationTestFilter();
    var settings = testExecutionHelper.CreateDotNetTestSettings(resultsPath, filter, framework, configuration);
    
    testExecutionHelper.RunDotNetTests(project, settings);
});

Task("_NetStandard_Integration_Tests_WithRetry")
    .IsDependentOn("Build.NetStandard")
    .Does(() =>
{
    Information("Running .NET Standard integration tests with retry...");
    
    var project = paths.Src.CombineWithFilePath("Ably.PubSub.Tests.DotNET/Ably.PubSub.Tests.DotNET.csproj");
    var resultsPath = paths.TestResults.CombineWithFilePath("tests-netstandard-integration.trx");
    
    var filter = testExecutionHelper.CreateIntegrationTestFilter();
    var settings = testExecutionHelper.CreateDotNetTestSettings(resultsPath, filter, framework, configuration);
    
    try
    {
        testExecutionHelper.RunDotNetTests(project, settings);
    }
    catch
    {
        Warning("Some tests failed. Retrying failed tests...");
    }
    
    testExecutionHelper.RetryFailedDotNetTests(project, resultsPath, testRetryHelper, framework, configuration);
});

///////////////////////////////////////////////////////////////////////////////
// PUBLIC TARGETS
///////////////////////////////////////////////////////////////////////////////

Task("Test.NetFramework.Unit")
    .Description("Run .NET Framework unit tests")
    .IsDependentOn("_NetFramework_Unit_Tests");

Task("Test.NetFramework.Unit.WithRetry")
    .Description("Run .NET Framework unit tests with retry on failure")
    .IsDependentOn("_NetFramework_Unit_Tests_WithRetry");

Task("Test.NetFramework.Integration")
    .Description("Run .NET Framework integration tests")
    .IsDependentOn("_NetFramework_Integration_Tests");

Task("Test.NetFramework.Integration.WithRetry")
    .Description("Run .NET Framework integration tests with retry on failure")
    .IsDependentOn("_NetFramework_Integration_Tests_WithRetry");

Task("Test.NetStandard.Unit")
    .Description("Run .NET Standard unit tests")
    .IsDependentOn("_NetStandard_Unit_Tests");

Task("Test.NetStandard.Unit.WithRetry")
    .Description("Run .NET Standard unit tests with retry on failure")
    .IsDependentOn("_NetStandard_Unit_Tests_WithRetry");

Task("Test.NetStandard.Integration")
    .Description("Run .NET Standard integration tests")
    .IsDependentOn("_NetStandard_Integration_Tests");

Task("Test.NetStandard.Integration.WithRetry")
    .Description("Run .NET Standard integration tests with retry on failure")
    .IsDependentOn("_NetStandard_Integration_Tests_WithRetry");

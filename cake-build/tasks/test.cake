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

    // Three passes, in separate processes, because these groups cannot share one. Every group is
    // green on its own and fails in numbers when combined, and the failures are always the same
    // shape: "timed out waiting for Connected". It is not ephemeral-port exhaustion (32 sockets in
    // TIME_WAIT against a 16384-port range), so it is contention on the shared sandbox app they
    // all reach.
    //
    // Measured, each alone: the repo's own sandbox specs green, UTS REST 58/58, UTS realtime
    // 33/33. Measured, the repo's specs sharing a pass with UTS REST: 52 failures, including 10
    // in ChannelSandboxSpecs - which passes 49/50 by itself. Collapsing any two of these back
    // together reintroduces that.
    //
    // Split on the `tier` trait rather than on the name: FullyQualifiedName has no negated-contains
    // operator, and vstest rejects `FullyQualifiedName!~x` as an invalid condition and then runs
    // nothing while still exiting 0 - so a name-based pass would silently test nothing.
    testExecutionHelper.RunDotNetTests(
        project,
        testExecutionHelper.CreateDotNetTestSettings(
            paths.TestResults.CombineWithFilePath("tests-netstandard-integration.trx"),
            testExecutionHelper.CreateIntegrationTestFilter() + "&tier!=realtime&tier!=uts-rest",
            framework,
            configuration));

    testExecutionHelper.RunDotNetTests(
        project,
        testExecutionHelper.CreateDotNetTestSettings(
            paths.TestResults.CombineWithFilePath("tests-netstandard-integration-uts-rest.trx"),
            testExecutionHelper.CreateIntegrationTestFilter() + "&tier=uts-rest",
            framework,
            configuration));

    testExecutionHelper.RunDotNetTests(
        project,
        testExecutionHelper.CreateDotNetTestSettings(
            paths.TestResults.CombineWithFilePath("tests-netstandard-integration-uts-realtime.trx"),
            testExecutionHelper.CreateIntegrationTestFilter() + "&tier=realtime",
            framework,
            configuration));
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

Task("_NetStandard_Proxy_Tests")
    .IsDependentOn("Build.NetStandard")
    .Does(() =>
{
    Information("Running UTS proxy tests (requires ably/uts-proxy)...");

    var project = paths.Src.CombineWithFilePath("Ably.PubSub.Tests.DotNET/Ably.PubSub.Tests.DotNET.csproj");
    var resultsPath = paths.TestResults.CombineWithFilePath("tests-netstandard-proxy.trx");

    var filter = testExecutionHelper.CreateProxyTestFilter();
    var settings = testExecutionHelper.CreateDotNetTestSettings(resultsPath, filter, framework, configuration);

    testExecutionHelper.RunDotNetTests(project, settings);
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

// Deliberately no .WithRetry variant. The proxy tier asserts on an event log that accumulates
// across a session, so a retry of a single test is not a clean repeat; and the retry wrappers
// swallow failures and exit 0, which would make this leg report nothing useful.
Task("Test.NetStandard.Proxy")
    .Description("Run the UTS proxy tests against the sandbox through ably/uts-proxy")
    .IsDependentOn("_NetStandard_Proxy_Tests");

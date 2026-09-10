///////////////////////////////////////////////////////////////////////////////
// TEST TASKS - NETFRAMEWORK (Internal)
///////////////////////////////////////////////////////////////////////////////

Task("_NetFramework_Unit_Tests")
    .IsDependentOn("Build.NetFramework")
    .Does(() =>
{
    Information("Running .NET Framework unit tests...");
    
    var testAssemblies = testExecutionHelper.FindTestAssemblies("IO.Ably.Tests.NETFramework");
    if (!testAssemblies.Any()) return;
    
    var settings = testExecutionHelper.CreateXUnitSettings("xunit-netframework-unit", isIntegration: false);
    testExecutionHelper.RunXUnitTests(testAssemblies, settings);
});

Task("_NetFramework_Unit_Tests_WithRetry")
    .IsDependentOn("Build.NetFramework")
    .Does(() =>
{
    Information("Running .NET Framework unit tests with retry...");
    
    var testAssemblies = testExecutionHelper.FindTestAssemblies("IO.Ably.Tests.NETFramework");
    if (!testAssemblies.Any()) return;
    
    var baseReportName = "xunit-netframework-unit";
    var resultsPath = paths.TestResults.CombineWithFilePath($"{baseReportName}.xml");
    
    var initialFailedTests = new List<string>();
    try
    {
        var settings = testExecutionHelper.CreateXUnitSettings(baseReportName, isIntegration: false);
        testExecutionHelper.RunXUnitTests(testAssemblies, settings);
    }
    catch
    {
        Warning("Some tests failed. Retrying failed tests...");
        initialFailedTests = testRetryHelper.FindFailedXUnitTests(resultsPath);
    }
    
    if (initialFailedTests.Any())
    {
        Information($"Retrying {initialFailedTests.Count} failed test(s)...");
        
        testExecutionHelper.RetryFailedXUnitTests(
            testAssemblies,
            resultsPath,
            testRetryHelper,
            (test) => testExecutionHelper.CreateXUnitSettings(baseReportName, isIntegration: false, isRetry: true)
        );
        
        // After retries complete, find all retry result files that were created
        var retryPattern = $"{baseReportName}-*.xml";
        var retryResultFiles = GetFiles(paths.TestResults.Combine(retryPattern).FullPath);
        
        // Analyze retry results
        var stillFailedTests = new List<string>();
        foreach (var retryResultPath in retryResultFiles)
        {
            var retryFailed = testRetryHelper.FindFailedXUnitTests(retryResultPath);
            if (retryFailed.Any())
            {
                stillFailedTests.AddRange(retryFailed);
            }
        }
        
        // Display summary
        testRetryHelper.DisplayRetryResultsSummary(".NET Framework Unit", initialFailedTests, stillFailedTests);
        
        // Validate and throw if non-flaky tests failed
        testRetryHelper.ValidateFlakyTests(stillFailedTests);
    }
});

Task("_NetFramework_Integration_Tests")
    .IsDependentOn("Build.NetFramework")
    .Does(() =>
{
    Information("Running .NET Framework integration tests...");
    
    var testAssemblies = testExecutionHelper.FindTestAssemblies("IO.Ably.Tests.NETFramework");
    if (!testAssemblies.Any()) return;
    
    var settings = testExecutionHelper.CreateXUnitSettings("xunit-netframework-integration", isIntegration: true);
    testExecutionHelper.RunXUnitTests(testAssemblies, settings);
});

Task("_NetFramework_Integration_Tests_WithRetry")
    .IsDependentOn("Build.NetFramework")
    .Does(() =>
{
    Information("Running .NET Framework integration tests with retry...");
    
    var testAssemblies = testExecutionHelper.FindTestAssemblies("IO.Ably.Tests.NETFramework");
    if (!testAssemblies.Any()) return;
    
    var baseReportName = "xunit-netframework-integration";
    var resultsPath = paths.TestResults.CombineWithFilePath($"{baseReportName}.xml");
    
    var initialFailedTests = new List<string>();
    try
    {
        var settings = testExecutionHelper.CreateXUnitSettings(baseReportName, isIntegration: true);
        testExecutionHelper.RunXUnitTests(testAssemblies, settings);
    }
    catch
    {
        Warning("Some tests failed. Retrying failed tests...");
        initialFailedTests = testRetryHelper.FindFailedXUnitTests(resultsPath);
    }
    
    if (initialFailedTests.Any())
    {
        Information($"Retrying {initialFailedTests.Count} failed test(s)...");
        
        testExecutionHelper.RetryFailedXUnitTests(
            testAssemblies,
            resultsPath,
            testRetryHelper,
            (test) => testExecutionHelper.CreateXUnitSettings(baseReportName, isIntegration: true, isRetry: true)
        );
        
        // After retries complete, find all retry result files that were created
        var retryPattern = $"{baseReportName}-*.xml";
        var retryResultFiles = GetFiles(paths.TestResults.Combine(retryPattern).FullPath);
        
        // Analyze retry results
        var stillFailedTests = new List<string>();
        foreach (var retryResultPath in retryResultFiles)
        {
            var retryFailed = testRetryHelper.FindFailedXUnitTests(retryResultPath);
            if (retryFailed.Any())
            {
                stillFailedTests.AddRange(retryFailed);
            }
        }
        
        // Display summary
        testRetryHelper.DisplayRetryResultsSummary(".NET Framework Integration", initialFailedTests, stillFailedTests);
        
        // Validate and throw if non-flaky tests failed
        testRetryHelper.ValidateFlakyTests(stillFailedTests);
    }
});

///////////////////////////////////////////////////////////////////////////////
// TEST TASKS - NETSTANDARD (Internal)
///////////////////////////////////////////////////////////////////////////////

Task("_NetStandard_Unit_Tests")
    .IsDependentOn("Build.NetStandard")
    .Does(() =>
{
    Information("Running .NET Standard unit tests...");
    
    var project = paths.Src.CombineWithFilePath("IO.Ably.Tests.DotNET/IO.Ably.Tests.DotNET.csproj");
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
    
    var project = paths.Src.CombineWithFilePath("IO.Ably.Tests.DotNET/IO.Ably.Tests.DotNET.csproj");
    var baseResultsName = "tests-netstandard-unit";
    var resultsPath = paths.TestResults.CombineWithFilePath($"{baseResultsName}.trx");
    
    var filter = testExecutionHelper.CreateUnitTestFilter(IsRunningOnUnix());
    var settings = testExecutionHelper.CreateDotNetTestSettings(resultsPath, filter, framework, configuration);
    
    var initialFailedTests = new List<string>();
    try
    {
        testExecutionHelper.RunDotNetTests(project, settings);
    }
    catch
    {
        Warning("Some tests failed. Retrying failed tests...");
        initialFailedTests = testRetryHelper.FindFailedDotNetTests(resultsPath);
    }
    
    if (initialFailedTests.Any())
    {
        Information($"Retrying {initialFailedTests.Count} failed test(s)...");
        
        testExecutionHelper.RetryFailedDotNetTests(project, resultsPath, testRetryHelper, framework, configuration);
        
        // After retries complete, find all retry result files that were created
        var retryPattern = $"{baseResultsName}-*.trx";
        var retryResultFiles = GetFiles(paths.TestResults.Combine(retryPattern).FullPath);
        
        // Analyze retry results
        var stillFailedTests = new List<string>();
        foreach (var retryResultPath in retryResultFiles)
        {
            var retryFailed = testRetryHelper.FindFailedDotNetTests(retryResultPath);
            if (retryFailed.Any())
            {
                stillFailedTests.AddRange(retryFailed);
            }
        }
        
        // Display summary
        testRetryHelper.DisplayRetryResultsSummary(".NET Standard Unit", initialFailedTests, stillFailedTests);
        
        // Validate and throw if non-flaky tests failed
        testRetryHelper.ValidateFlakyTests(stillFailedTests);
    }
});

Task("_NetStandard_Integration_Tests")
    .IsDependentOn("Build.NetStandard")
    .Does(() =>
{
    Information("Running .NET Standard integration tests...");
    
    var project = paths.Src.CombineWithFilePath("IO.Ably.Tests.DotNET/IO.Ably.Tests.DotNET.csproj");
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
    
    var project = paths.Src.CombineWithFilePath("IO.Ably.Tests.DotNET/IO.Ably.Tests.DotNET.csproj");
    var baseResultsName = "tests-netstandard-integration";
    var resultsPath = paths.TestResults.CombineWithFilePath($"{baseResultsName}.trx");
    
    var filter = testExecutionHelper.CreateIntegrationTestFilter();
    var settings = testExecutionHelper.CreateDotNetTestSettings(resultsPath, filter, framework, configuration);
    
    var initialFailedTests = new List<string>();
    try
    {
        testExecutionHelper.RunDotNetTests(project, settings);
    }
    catch
    {
        Warning("Some tests failed. Retrying failed tests...");
        initialFailedTests = testRetryHelper.FindFailedDotNetTests(resultsPath);
    }
    
    if (initialFailedTests.Any())
    {
        Information($"Retrying {initialFailedTests.Count} failed test(s)...");
        
        testExecutionHelper.RetryFailedDotNetTests(project, resultsPath, testRetryHelper, framework, configuration);
        
        // After retries complete, find all retry result files that were created
        var retryPattern = $"{baseResultsName}-*.trx";
        var retryResultFiles = GetFiles(paths.TestResults.Combine(retryPattern).FullPath);
        
        // Analyze retry results
        var stillFailedTests = new List<string>();
        foreach (var retryResultPath in retryResultFiles)
        {
            var retryFailed = testRetryHelper.FindFailedDotNetTests(retryResultPath);
            if (retryFailed.Any())
            {
                stillFailedTests.AddRange(retryFailed);
            }
        }
        
        // Display summary
        testRetryHelper.DisplayRetryResultsSummary(".NET Standard Integration", initialFailedTests, stillFailedTests);
        
        // Validate and throw if non-flaky tests failed
        testRetryHelper.ValidateFlakyTests(stillFailedTests);
    }
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

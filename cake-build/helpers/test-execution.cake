///////////////////////////////////////////////////////////////////////////////
// TEST EXECUTION HELPERS
///////////////////////////////////////////////////////////////////////////////

public class TestExecutionHelper
{
    // A single test that makes no progress for this long is treated as hung: the test host is killed and the
    // run fails, naming the test, instead of the job stalling silently until it is cancelled.
    // The slowest healthy sandbox test takes under 1.5 minutes.
    private const string TestHangTimeout = "5m";

    private readonly ICakeContext _context;
    private readonly BuildPaths _paths;
    private readonly FrameworkDetector _frameworkDetector;
    
    public TestExecutionHelper(ICakeContext context, BuildPaths paths, FrameworkDetector frameworkDetector)
    {
        _context = context;
        _paths = paths;
        _frameworkDetector = frameworkDetector;
    }
    
    /// <summary>
    /// Creates XUnit2 settings with common configuration
    /// </summary>
    public XUnit2Settings CreateXUnitSettings(string reportName, bool isIntegration = false, bool isRetry = false)
    {
        var settings = new XUnit2Settings
        {
            ToolPath = _paths.Root.CombineWithFilePath("tools/xunit/xunit.console.exe"),
            OutputDirectory = _paths.TestResults,
            XmlReport = true,
            ReportName = reportName
        };
        
        if (isIntegration)
        {
            settings.Parallelism = ParallelismOption.Collections;
            settings.ArgumentCustomization = args => args
                .Append("-trait")
                .AppendQuoted("type=integration")
                .Append("-maxthreads")
                .Append("unlimited")
                .Append("-verbose");
        }
        else if (!isRetry)
        {
            // Unit tests exclude integration trait
            settings.ArgumentCustomization = args => args
                .Append("-notrait")
                .AppendQuoted("type=integration")
                .Append("-verbose");
        }
        else
        {
            // Retry mode - just verbose
            settings.ArgumentCustomization = args => args
                .Append("-verbose");
        }
        
        return settings;
    }
    
    /// <summary>
    /// Creates DotNetTest settings with common configuration
    /// </summary>
    public DotNetTestSettings CreateDotNetTestSettings(
        FilePath resultsPath, 
        string filter, 
        string framework = null,
        string configuration = "Release")
    {
        var settings = new DotNetTestSettings
        {
            Configuration = configuration,
            Filter = filter,
            Loggers = new[] {
                $"trx;logfilename={resultsPath.FullPath}",
                "console;verbosity=detailed"
            },
            Verbosity = DotNetVerbosity.Normal,
            NoBuild = true,
            NoRestore = true,
            ArgumentCustomization = AppendHangTimeout
        };
        
        if (!string.IsNullOrEmpty(framework))
        {
            ValidateFramework(framework);
            settings.Framework = framework;
        }
        
        return settings;
    }
    
    private static ProcessArgumentBuilder AppendHangTimeout(ProcessArgumentBuilder args)
    {
        return args
            .Append("--blame-hang-timeout")
            .Append(TestHangTimeout)
            .Append("--blame-hang-dump-type")
            .Append("none");
    }

    /// <summary>
    /// Validates if a framework is available and warns if not
    /// </summary>
    public void ValidateFramework(string framework)
    {
        if (!_frameworkDetector.IsFrameworkAvailable(framework))
        {
            var available = string.Join(", ", _frameworkDetector.GetTargetFrameworks());
            _context.Warning($"Framework '{framework}' may not be available. Available frameworks: {available}");
            _context.Warning("Attempting to run tests anyway...");
        }
    }
    
    /// <summary>
    /// Finds test assemblies matching a pattern in a project directory
    /// </summary>
    public FilePathCollection FindTestAssemblies(string projectRelativePath, string pattern = "Ably.PubSub.Tests.*.dll")
    {
        var projectPath = _paths.Src.Combine(projectRelativePath);
        var searchPath = projectPath.Combine("bin/Release").Combine(pattern).FullPath;
        var testAssemblies = _context.GetFiles(searchPath);
        
        if (!testAssemblies.Any())
        {
            throw new Exception($"No test assemblies found matching pattern: {searchPath}. " +
                "A green run with zero assemblies is never intended; check the build output and the pattern.");
        }

        return testAssemblies;
    }
    
    /// <summary>
    /// Runs XUnit tests with the specified settings
    /// </summary>
    public void RunXUnitTests(FilePathCollection assemblies, XUnit2Settings settings)
    {
        if (!assemblies.Any())
        {
            _context.Warning("No test assemblies provided, skipping test execution");
            return;
        }
        
        _context.XUnit2(assemblies, settings);
    }
    
    /// <summary>
    /// Runs DotNet tests with the specified settings
    /// </summary>
    public void RunDotNetTests(FilePath project, DotNetTestSettings settings)
    {
        if (!_context.FileExists(project))
        {
            _context.Warning($"Test project not found: {project}");
            return;
        }
        
        _context.DotNetTest(project.FullPath, settings);
    }
    
    /// <summary>
    /// Retries failed XUnit tests
    /// </summary>
    public void RetryFailedXUnitTests(
        FilePathCollection assemblies, 
        FilePath resultsPath,
        TestRetryHelper retryHelper,
        Func<string, XUnit2Settings> createRetrySettings)
    {
        // Unlike the dotnet test retry, this one does not fail the task: FindFailedXUnitTests does not yet
        // recognise failures in the xunit v2 report, and the .NET Framework legs have long-standing failures
        // that this has hidden. Making it strict needs those fixed first.
        var failedTests = retryHelper.FindFailedXUnitTests(resultsPath);
        _context.Information($"Found {failedTests.Count} failed tests to retry");

        foreach (var test in failedTests)
        {
            _context.Information($"Retrying test: {test}");
            
            var retryResultsPath = retryHelper.GetNextTestResultPath(resultsPath);
            var settings = createRetrySettings(test);
            settings.ReportName = retryResultsPath.GetFilenameWithoutExtension().FullPath;
            
            // Add method filter for retry
            var baseCustomization = settings.ArgumentCustomization;
            settings.ArgumentCustomization = args => {
                var result = baseCustomization != null ? baseCustomization(args) : args;
                return result
                    .Append("-method")
                    .AppendQuoted(test);
            };
            
            try
            {
                _context.XUnit2(assemblies, settings);
            }
            catch
            {
                _context.Warning($"Test {test} failed on retry");
            }
        }
    }
    
    /// <summary>
    /// Retries failed DotNet tests
    /// </summary>
    public void RetryFailedDotNetTests(
        FilePath project,
        FilePath resultsPath,
        TestRetryHelper retryHelper,
        string framework,
        string configuration = "Release")
    {
        // A hung or crashed test host leaves the tests it had not yet run out of the results, so retrying the
        // failures it did record could turn the run green without those tests ever having run.
        var abortReason = retryHelper.FindDotNetTestRunAbortReason(resultsPath);
        if (abortReason != null)
        {
            throw new Exception($"The test run was aborted, so failed tests are not retried: {abortReason}");
        }

        var failedTests = retryHelper.FindFailedDotNetTests(resultsPath);
        _context.Information($"Found {failedTests.Count} failed tests to retry");

        if (!failedTests.Any())
        {
            throw new Exception($"The test run failed but no failed tests were found in {resultsPath} to retry.");
        }

        var stillFailing = new List<string>();
        foreach (var test in failedTests)
        {
            if (!RetryDotNetTest(project, resultsPath, retryHelper, framework, configuration, test))
            {
                stillFailing.Add(test);
            }
        }

        ThrowIfStillFailing(stillFailing);
    }

    // Sandbox tests have some known flakes, so a failed test gets more than one retry; a test that fails
    // every attempt fails the task.
    private const int MaxRetriesPerTest = 2;

    private bool RetryDotNetTest(
        FilePath project,
        FilePath resultsPath,
        TestRetryHelper retryHelper,
        string framework,
        string configuration,
        string test)
    {
        for (var attempt = 1; attempt <= MaxRetriesPerTest; attempt++)
        {
            _context.Information($"Retrying test ({attempt}/{MaxRetriesPerTest}): {test}");

            var retryResultsPath = retryHelper.GetNextTestResultPath(resultsPath, ".trx");

            var retrySettings = new DotNetTestSettings
            {
                Configuration = configuration,
                Filter = test,
                Loggers = new[] {
                    $"trx;logfilename={retryResultsPath.FullPath}",
                    "console;verbosity=detailed"
                },
                Verbosity = DotNetVerbosity.Normal,
                NoBuild = true,
                NoRestore = true,
                ArgumentCustomization = AppendHangTimeout
            };

            if (!string.IsNullOrEmpty(framework))
            {
                retrySettings.Framework = framework;
            }

            try
            {
                _context.DotNetTest(project.FullPath, retrySettings);
                return true;
            }
            catch
            {
                _context.Warning($"Test {test} failed on retry {attempt}/{MaxRetriesPerTest}");
            }
        }

        return false;
    }

    private void ThrowIfStillFailing(List<string> stillFailing)
    {
        if (stillFailing.Any())
        {
            throw new Exception($"{stillFailing.Count} test(s) failed on every retry: {string.Join(", ", stillFailing)}");
        }
    }
    
    /// <summary>
    /// Creates filter for DotNet unit tests (excludes integration tests)
    /// </summary>
    public string CreateUnitTestFilter(bool isUnix = false)
    {
        var filters = new List<string> { "type!=integration" };
        
        if (isUnix)
        {
            filters.Add("linux!=skip");
        }
        
        return string.Join("&", filters);
    }
    
    /// <summary>
    /// Creates filter for DotNet integration tests
    /// </summary>
    public string CreateIntegrationTestFilter()
    {
        return "type=integration";
    }
}

var testExecutionHelper = new TestExecutionHelper(Context, paths, frameworkDetector);

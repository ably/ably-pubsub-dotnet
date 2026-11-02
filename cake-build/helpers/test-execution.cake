///////////////////////////////////////////////////////////////////////////////
// TEST EXECUTION HELPERS
///////////////////////////////////////////////////////////////////////////////

public class TestExecutionHelper
{
    /// <summary>
    /// UTS tiers. Every UTS test class carries [Trait("uts", "<tier>")]; UTS infra tests also
    /// carry [Trait("uts-infra", "common|<tier>")]. They run from the Test.*.Uts and
    /// Test.*.UtsInfra targets, one tier per run; the native unit and integration targets
    /// exclude all of them, so each UTS suite has exactly one CI home.
    /// </summary>
    public static readonly string[] UtsTiers = { "unit", "integration", "proxy" };

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
        var settings = NewXUnitSettings(reportName);
        
        if (isIntegration)
        {
            settings.Parallelism = ParallelismOption.Collections;
            settings.ArgumentCustomization = args => ExcludeUtsTraits(args)
                .Append("-trait")
                .AppendQuoted("type=integration")
                .Append("-maxthreads")
                .Append("unlimited")
                .Append("-verbose");
        }
        else if (!isRetry)
        {
            // Unit tests exclude integration trait
            settings.ArgumentCustomization = args => ExcludeUtsTraits(args)
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
    /// Creates XUnit2 settings for a UTS run: only tests carrying one of the given name=value
    /// traits. xunit console 2.4 ORs repeated -trait options (and ANDs repeated -notrait).
    /// </summary>
    public XUnit2Settings CreateUtsXUnitSettings(string reportName, IEnumerable<string> traits)
    {
        var settings = NewXUnitSettings(reportName);
        settings.ArgumentCustomization = args =>
        {
            foreach (var trait in traits)
            {
                args.Append("-trait").AppendQuoted(trait);
            }
            return args.Append("-verbose");
        };

        return settings;
    }

    private XUnit2Settings NewXUnitSettings(string reportName)
    {
        return new XUnit2Settings
        {
            ToolPath = _paths.Root.CombineWithFilePath("tools/xunit/xunit.console.exe"),
            OutputDirectory = _paths.TestResults,
            XmlReport = true,
            ReportName = reportName
        };
    }

    private static ProcessArgumentBuilder ExcludeUtsTraits(ProcessArgumentBuilder args)
    {
        foreach (var tier in UtsTiers)
        {
            args.Append("-notrait").AppendQuoted($"uts={tier}");
        }
        return args;
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
            NoRestore = true
        };
        
        if (!string.IsNullOrEmpty(framework))
        {
            ValidateFramework(framework);
            settings.Framework = framework;
        }
        
        return settings;
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
        var failedTests = retryHelper.FindFailedDotNetTests(resultsPath);
        _context.Information($"Found {failedTests.Count} failed tests to retry");
        
        foreach (var test in failedTests)
        {
            _context.Information($"Retrying test: {test}");
            
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
                NoRestore = true
            };
            
            if (!string.IsNullOrEmpty(framework))
            {
                retrySettings.Framework = framework;
            }
            
            try
            {
                _context.DotNetTest(project.FullPath, retrySettings);
            }
            catch
            {
                _context.Warning($"Test {test} failed on retry");
            }
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

        filters.Add(UtsExclusionFilter());
        
        return string.Join("&", filters);
    }
    
    /// <summary>
    /// Creates filter for DotNet integration tests
    /// </summary>
    public string CreateIntegrationTestFilter()
    {
        return "type=integration&" + UtsExclusionFilter();
    }

    /// <summary>
    /// Excludes every UTS tier. A test without a uts trait satisfies each uts!=tier condition
    /// (VSTest NotEqual is true when the property is absent), so native tests are unaffected.
    /// </summary>
    private static string UtsExclusionFilter()
    {
        return string.Join("&", UtsTiers.Select(tier => $"uts!={tier}"));
    }

    /// <summary>
    /// Returns the tier when it is one of UtsTiers, else throws
    /// </summary>
    public string ValidateUtsTier(string tier)
    {
        if (string.IsNullOrEmpty(tier) || !UtsTiers.Contains(tier))
        {
            throw new Exception($"--uts-tier must be one of: {string.Join(", ", UtsTiers)} (got '{tier}')");
        }

        return tier;
    }

    /// <summary>
    /// Creates filter for DotNet UTS tests of one tier (generated and infra tests)
    /// </summary>
    public string CreateUtsTestFilter(string tier)
    {
        return $"uts={ValidateUtsTier(tier)}";
    }

    /// <summary>
    /// Creates filter for DotNet UTS infra tests of one tier, plus the common infra self-tests
    /// </summary>
    public string CreateUtsInfraTestFilter(string tier)
    {
        return $"uts-infra=common|uts-infra={ValidateUtsTier(tier)}";
    }

    /// <summary>
    /// xunit console traits for UTS tests of one tier
    /// </summary>
    public IEnumerable<string> UtsXUnitTraits(string tier)
    {
        return new[] { $"uts={ValidateUtsTier(tier)}" };
    }

    /// <summary>
    /// xunit console traits for UTS infra tests of one tier, plus the common infra self-tests
    /// </summary>
    public IEnumerable<string> UtsInfraXUnitTraits(string tier)
    {
        return new[] { "uts-infra=common", $"uts-infra={ValidateUtsTier(tier)}" };
    }

    /// <summary>
    /// Fails when a results file (trx or xunit v2 xml) is missing or reports zero tests. A UTS
    /// target that selects nothing must not pass green: dotnet test exits 0 when no test matches
    /// the filter, and so does xunit console when no test matches the traits.
    /// </summary>
    public void EnsureTestsRan(FilePath resultsPath)
    {
        if (!_context.FileExists(resultsPath))
        {
            throw new Exception($"No test results at {resultsPath.FullPath}; cannot confirm that any test ran.");
        }

        var xml = System.Text.RegularExpressions.Regex.Replace(
            System.IO.File.ReadAllText(resultsPath.FullPath), @"xmlns=""[^""]+""", "");
        var doc = System.Xml.Linq.XDocument.Parse(xml);

        // trx: TestRun/ResultSummary/Counters@total; xunit v2: assemblies/assembly@total
        var total = doc.Descendants("Counters").Concat(doc.Descendants("assembly"))
            .Select(element => (string)element.Attribute("total"))
            .Where(value => !string.IsNullOrEmpty(value))
            .Sum(value => int.Parse(value));

        if (total == 0)
        {
            throw new Exception($"{resultsPath.GetFilename()} reports zero tests; the filter selected nothing.");
        }

        _context.Information($"{resultsPath.GetFilename()}: {total} tests");
    }
}

var testExecutionHelper = new TestExecutionHelper(Context, paths, frameworkDetector);

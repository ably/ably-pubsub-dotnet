using System.Xml.XPath;

///////////////////////////////////////////////////////////////////////////////
// TEST RETRY HELPERS
///////////////////////////////////////////////////////////////////////////////

public class TestRetryHelper
{
    private readonly ICakeContext _context;
    
    public TestRetryHelper(ICakeContext context)
    {
        _context = context;
    }
    
    public List<string> FindFailedXUnitTests(FilePath resultsPath)
    {
        var failedTests = new List<string>();
        
        if (!_context.FileExists(resultsPath))
        {
            _context.Warning($"Results file not found: {resultsPath}");
            return failedTests;
        }
        
        try
        {
            var doc = System.Xml.Linq.XDocument.Load(resultsPath.FullPath);
            
            // XUnit v2 XML format uses @result='Fail' attribute
            // Try both formats for compatibility
            var failedNodes = doc.XPathSelectElements("//test[@result='Fail']")
                .Concat(doc.XPathSelectElements("//test-case[@result='Fail']"))
                .Concat(doc.XPathSelectElements("//test-case[@success='False']"));
            
            foreach (var node in failedNodes)
            {
                var testName = node.Attribute("name")?.Value;
                if (!string.IsNullOrEmpty(testName))
                {
                    var trimmedName = TrimTestMethod(testName);
                    if (!failedTests.Contains(trimmedName))
                    {
                        _context.Information($"Found failed test: {trimmedName}");
                        failedTests.Add(trimmedName);
                    }
                }
            }
            
            _context.Information($"Total failed tests found: {failedTests.Count}");
        }
        catch (Exception ex)
        {
            _context.Warning($"Error parsing XUnit results: {ex.Message}");
            _context.Warning($"Stack trace: {ex.StackTrace}");
        }
        
        return failedTests;
    }
    
    public List<string> FindFailedDotNetTests(FilePath resultsPath)
    {
        var failedTests = new List<string>();
        
        if (!_context.FileExists(resultsPath))
            return failedTests;
        
        try
        {
            var xml = System.IO.File.ReadAllText(resultsPath.FullPath);
            // Remove namespace for easier XPath queries
            xml = System.Text.RegularExpressions.Regex.Replace(
                xml, @"xmlns=""[^""]+""", "");
            
            var doc = System.Xml.Linq.XDocument.Parse(xml);
            var nodes = doc.XPathSelectElements("//UnitTestResult[@outcome='Failed']");
            
            foreach (var node in nodes)
            {
                var testName = node.Attribute("testName")?.Value;
                if (!string.IsNullOrEmpty(testName))
                {
                    failedTests.Add(TrimTestMethod(testName));
                }
            }
        }
        catch (Exception ex)
        {
            _context.Warning($"Error parsing dotnet test results: {ex.Message}");
        }
        
        return failedTests;
    }
    
    private string TrimTestMethod(string testMethod)
    {
        if (testMethod.Contains("("))
        {
            return testMethod.Substring(0, testMethod.IndexOf("("));
        }
        return testMethod;
    }
    
    public FilePath GetNextTestResultPath(FilePath basePath, string extension = ".xml")
    {
        for (int i = 1; i <= 100; i++)
        {
            var newPath = basePath.FullPath.Replace(extension, $"-{i}{extension}");
            if (!_context.FileExists(newPath))
            {
                return new FilePath(newPath);
            }
        }
        return basePath;
    }
    
    /// <summary>
    /// Displays a formatted summary table of test retry results
    /// </summary>
    /// <param name="testType">The type of tests (e.g., ".NET Framework Unit", ".NET Standard Integration")</param>
    /// <param name="initialFailedTests">List of tests that failed initially</param>
    /// <param name="stillFailedTests">List of tests that still failed after retry</param>
    public void DisplayRetryResultsSummary(string testType, List<string> initialFailedTests, List<string> stillFailedTests)
    {
        var passedTests = initialFailedTests.Except(stillFailedTests).ToList();
        
        if (passedTests.Any())
        {
            _context.Information("");
            _context.Information("✓ Tests that PASSED on retry:");
            foreach (var test in passedTests)
            {
                _context.Information($"  • {test}");
            }
        }
        
        if (stillFailedTests.Any())
        {
            _context.Information("");
            _context.Warning("✗ Tests that FAILED after retry:");
            foreach (var test in stillFailedTests)
            {
                _context.Warning($"  • {test}");
            }
        }
        else
        {
            _context.Information("");
            _context.Information("✓ All retried tests passed!");
        }
        
        _context.Information("");
    }
    
    /// <summary>
    /// Validates failed tests and throws an exception if any non-flaky tests failed.
    /// Tests ending with "_Flaky" are ignored.
    /// </summary>
    /// <param name="stillFailedTests">List of tests that still failed after retry</param>
    public void ValidateFlakyTests(List<string> stillFailedTests)
    {
        if (!stillFailedTests.Any())
        {
            return;
        }
        
        var nonFlakyFailedTests = stillFailedTests.Where(test => !test.EndsWith("_Flaky")).ToList();
        
        if (nonFlakyFailedTests.Any())
        {
            _context.Error("");
            _context.Error($"✗ {nonFlakyFailedTests.Count} non-flaky test(s) failed after retry:");
            foreach (var test in nonFlakyFailedTests)
            {
                _context.Error($"  • {test}");
            }
            _context.Error("");
            
            throw new Exception($"{nonFlakyFailedTests.Count} test(s) failed after retry");
        }
        else
        {
            _context.Information("");
            _context.Information($"ℹ All {stillFailedTests.Count} failed test(s) are marked as flaky (ending with '_Flaky')");
            _context.Information("");
        }
    }
}

var testRetryHelper = new TestRetryHelper(Context);

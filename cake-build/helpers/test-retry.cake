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
            return failedTests;
        
        try
        {
            var doc = System.Xml.Linq.XDocument.Load(resultsPath.FullPath);
            var nodes = doc.XPathSelectElements("//test-case[@success='False']");
            
            foreach (var node in nodes)
            {
                var testName = node.Attribute("name")?.Value;
                if (!string.IsNullOrEmpty(testName))
                {
                    failedTests.Add(TrimTestMethod(testName));
                }
            }
        }
        catch (Exception ex)
        {
            _context.Warning($"Error parsing XUnit results: {ex.Message}");
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
    
    /// <summary>
    /// Returns why the dotnet test run recorded in the given TRX file was aborted (for example a test host
    /// killed by the hang timeout, or a crash), or null if it ran to completion.
    /// </summary>
    public string FindDotNetTestRunAbortReason(FilePath resultsPath)
    {
        if (!_context.FileExists(resultsPath))
            return $"no test results were written to {resultsPath}";

        try
        {
            var xml = System.IO.File.ReadAllText(resultsPath.FullPath);
            xml = System.Text.RegularExpressions.Regex.Replace(
                xml, @"xmlns=""[^""]+""", "");

            var doc = System.Xml.Linq.XDocument.Parse(xml);
            var summary = doc.XPathSelectElement("//ResultSummary");
            if (summary?.Attribute("outcome")?.Value == "Aborted")
            {
                return "the result summary outcome is Aborted";
            }

            var reasons = doc.XPathSelectElements("//ResultSummary/RunInfos/RunInfo")
                .Select(x => x.Element("Text")?.Value ?? string.Empty)
                .Where(x => x.IndexOf("aborted", StringComparison.OrdinalIgnoreCase) >= 0
                    || x.IndexOf("inactivity time", StringComparison.OrdinalIgnoreCase) >= 0)
                .ToList();

            return reasons.Any() ? string.Join(" ", reasons) : null;
        }
        catch (Exception ex)
        {
            return $"the test results could not be parsed: {ex.Message}";
        }
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
}

var testRetryHelper = new TestRetryHelper(Context);

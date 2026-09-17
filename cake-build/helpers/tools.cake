///////////////////////////////////////////////////////////////////////////////
// TOOL HELPERS
///////////////////////////////////////////////////////////////////////////////

public class ILRepackHelper
{
    private readonly ICakeContext _context;
    
    public ILRepackHelper(ICakeContext context)
    {
        _context = context;
    }
    
    public void MergeDLLs(FilePath primaryDll, FilePath[] dllsToMerge, FilePath outputDll, FilePath internalizeExcludeFile = null)
    {
        if (!_context.FileExists(primaryDll))
        {
            throw new Exception($"Primary DLL not found: {primaryDll}");
        }
        
        if (dllsToMerge == null || dllsToMerge.Length == 0)
        {
            throw new ArgumentException("At least one DLL must be specified to merge");
        }
        
        // Get the root directory (parent of cake-build)
        var rootDir = _context.MakeAbsolute(_context.Directory("../"));
        var ilRepackPath = rootDir.CombineWithFilePath("tools/ilrepack.exe");
        
        // Build list of DLL paths - primary first, then merging DLLs
        var dllPaths = new List<string> { $"\"{primaryDll.FullPath}\"" };
        var mergingDllNames = new List<string>();
        
        foreach (var dllPath in dllsToMerge)
        {
            if (_context.FileExists(dllPath))
            {
                dllPaths.Add($"\"{dllPath.FullPath}\"");
                mergingDllNames.Add(dllPath.GetFilename().ToString());
            }
            else
            {
                throw new Exception($"Merge dll not found at path \"{dllPath.FullPath}\"");
            }
        }
        
        _context.Information($"Merging {string.Join(", ", mergingDllNames)} into {primaryDll.GetFilename()}...");
        
        // Ensure output directory exists
        var outputDir = outputDll.GetDirectory();
        _context.EnsureDirectoryExists(outputDir);
        
        // Get the directory containing the primary DLL for assembly resolution
        var binDir = primaryDll.GetDirectory();
        
        // Build ILRepack arguments - explicitly merge only the DLLs we specify
        var args = new ProcessArgumentBuilder()
            .Append("/targetplatform:v4")
            .Append(internalizeExcludeFile != null && _context.FileExists(internalizeExcludeFile)
                ? $"/internalize:\"{internalizeExcludeFile.FullPath}\""
                : "/internalize")
            // /lib: Specifies where ILRepack should search for referenced assemblies when loading the primary DLL.
            // This is needed because Mono.Cecil (used by ILRepack) must resolve all type references while reading
            // the assembly metadata, even before the merge begins. Without this, it fails to resolve types from
            // dependencies like Newtonsoft.Json that are referenced in custom attributes or type signatures.
            .Append($"/lib:\"{binDir.FullPath}\"")
            .Append($"/attr:\"{primaryDll.FullPath}\"")
            .Append($"/keyfile:\"{rootDir.CombineWithFilePath("IO.Ably.snk").FullPath}\"")
            .Append("/parallel")
            .Append($"/out:\"{outputDll.FullPath}\"");

        // When ILRepack runs under Mono (macOS/Linux), the netstandard2.0 inputs' 'netstandard'
        // facade lives in Mono's 4.5/Facades directory, which /targetplatform:v4 does not search;
        // add it as an extra /lib so Mono.Cecil can resolve it. No-op on Windows (paths absent).
        var monoFacadesCandidates = new[]
        {
            new DirectoryPath("/Library/Frameworks/Mono.framework/Versions/Current/lib/mono/4.5/Facades"),
            new DirectoryPath("/usr/lib/mono/4.5/Facades")
        };
        foreach (var facades in monoFacadesCandidates)
        {
            if (_context.DirectoryExists(facades))
            {
                args.Append($"/lib:\"{facades.FullPath}\"");
                break;
            }
        }
        
        // Add all DLL paths explicitly (primary + merging DLLs)
        foreach (var dllPath in dllPaths)
        {
            args.Append(dllPath);
        }
        
        // Use ILRepack to merge all DLLs in one go
        var exitCode = _context.StartProcess(ilRepackPath.FullPath, new ProcessSettings
        {
            Arguments = args
        });
        
        if (exitCode != 0)
        {
            throw new Exception($"ILRepack failed with exit code {exitCode}");
        }
        
        _context.Information($"✓ Merged assembly created at {outputDll}");
    }
}

var ilRepackHelper = new ILRepackHelper(Context);


///////////////////////////////////////////////////////////////////////////////
// PATHS
///////////////////////////////////////////////////////////////////////////////

public class BuildPaths
{
    public DirectoryPath Root { get; }
    public DirectoryPath Src { get; }
    public DirectoryPath Lib { get; }
    public DirectoryPath TestResults { get; }
    
    public FilePath MainSolution { get; }
    public FilePath NetStandardSolution { get; }
    public FilePath NetFrameworkSolution { get; }
    public FilePath PackageSolution { get; }
    public FilePath DeltaCodecProject { get; }

    // Single source of truth for the committed merged Unity plugin, referenced by
    // the build task (output) and the Unity packaging guard so the two sites
    // cannot drift.
    public FilePath UnityPluginDll { get; }
    
    public BuildPaths(ICakeContext context)
    {
        // Get the actual repository root (parent of cake-build directory)
        Root = context.MakeAbsolute(context.Directory("../"));
        Src = Root.Combine("src");
        Lib = Root.Combine("lib");
        TestResults = Root.Combine("test-results");
        
        MainSolution = Src.CombineWithFilePath("Ably.PubSub.sln");
        NetStandardSolution = Src.CombineWithFilePath("Ably.PubSub.NetStandard.sln");
        NetFrameworkSolution = Src.CombineWithFilePath("Ably.PubSub.NetFramework.sln");
        PackageSolution = Src.CombineWithFilePath("Ably.PubSub.Package.sln");
        
        DeltaCodecProject = Lib.CombineWithFilePath("delta-codec/IO.Ably.DeltaCodec/IO.Ably.DeltaCodec.csproj");

        UnityPluginDll = Root.CombineWithFilePath("unity/Assets/Ably/Plugins/Ably.PubSub.Device.dll");
    }
}

var paths = new BuildPaths(Context);

using System.Reflection;
using System.Runtime.CompilerServices;

// General Information about an assembly is controlled through the following
// set of attributes. Change these attribute values to modify the information
// associated with an assembly.
[assembly: AssemblyTitle("Ably.PubSub.Core")]
[assembly: AssemblyConfiguration("")]
[assembly: AssemblyTrademark("")]
[assembly: AssemblyCulture("")]
[assembly: AssemblyDescription("Client for ably.com realtime service")]
#if !PACKAGE
[assembly: InternalsVisibleTo("Ably.PubSub.Tests.DotNET")]
#endif
#if UNITY_PACKAGE
[assembly: InternalsVisibleTo("Unity.Assets.Tests.AblySandbox")]
[assembly: InternalsVisibleTo("Unity.Assets.Tests.EditMode")]
[assembly: InternalsVisibleTo("Unity.Assets.Tests.PlayMode")]
#endif

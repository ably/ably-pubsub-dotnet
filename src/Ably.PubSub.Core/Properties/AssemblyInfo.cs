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

// The door assemblies construct the core clients through their internal constructors. Outside
// Package builds nothing is signed (signing is Package-conditional, in lockstep across the core
// and the doors), so the grants are keyless here.
[assembly: InternalsVisibleTo("Ably.PubSub.Device")]
[assembly: InternalsVisibleTo("Ably.PubSub.Server")]
#else
// Package builds sign every Ably assembly with Ably.PubSub.snk, and InternalsVisibleTo names the
// consuming assembly's identity, so the door grants must carry the matching public key
// (from `sn -tp` on Ably.PubSub.snk).
[assembly: InternalsVisibleTo("Ably.PubSub.Device, PublicKey=002400000480000094000000060200000024000052534131000400000100010001394bb0af9eb8e04f43676c91691de20f2137847e153e27bb96cf2dedf43bce3073f699ca136fb7f9eea0d9b9c6748e9c0be5543761945e101062f8770129512c4c397a08c1b459357e7a49a4dfd7e16ac9c84d1ab3fe1177b3e7741ea10eba746433691bbf1ad643bdf25bcf397a384f96e8d138b129bdb663189200d33dcf")]
[assembly: InternalsVisibleTo("Ably.PubSub.Server, PublicKey=002400000480000094000000060200000024000052534131000400000100010001394bb0af9eb8e04f43676c91691de20f2137847e153e27bb96cf2dedf43bce3073f699ca136fb7f9eea0d9b9c6748e9c0be5543761945e101062f8770129512c4c397a08c1b459357e7a49a4dfd7e16ac9c84d1ab3fe1177b3e7741ea10eba746433691bbf1ad643bdf25bcf397a384f96e8d138b129bdb663189200d33dcf")]
#endif
#if UNITY_PACKAGE
[assembly: InternalsVisibleTo("Unity.Assets.Tests.AblySandbox")]
[assembly: InternalsVisibleTo("Unity.Assets.Tests.EditMode")]
[assembly: InternalsVisibleTo("Unity.Assets.Tests.PlayMode")]
#endif

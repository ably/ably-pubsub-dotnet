using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using FluentAssertions;
using Xunit;

namespace IO.Ably.Tests.PubSub
{
    /// <summary>
    /// The lockstep packaging contract, asserted from the nuspec files themselves.
    ///
    /// The same assertions live in the Cake release pre-flight (`cake-build/tasks/release.cake`),
    /// which is what guards the actual release. They are duplicated here so that a PR which edits
    /// a nuspec fails the ordinary unit test run rather than waiting for a packaging job: the
    /// mistakes being guarded against - a door pinned loosely, a resurrected `ably.io` package, a
    /// hard-coded version - are all silent at build time and only visible to a consumer after the
    /// version has been published and can never be changed.
    ///
    /// What cannot be asserted here is the packed output: whether nuget substitutes `$version$`
    /// inside a dependency's version attribute is a property of the packer, not of the source, so
    /// it is asserted post-pack by `_Release_Verify_Packages` against the produced .nupkg files.
    /// </summary>
    public class PackagingSpecs
    {
        private const string CorePackageId = "Ably.PubSub.Core";

        /// <summary>
        /// The exact set of packages released from this repository, nuspec file name to package id.
        /// Every one of them ships at the same version from the same run.
        /// </summary>
        private static readonly IReadOnlyDictionary<string, string> PackageSet = new Dictionary<string, string>
        {
            { "ably.pubsub.core.nuspec", CorePackageId },
            { "ably.pubsub.device.nuspec", "Ably.PubSub.Device" },
            { "ably.pubsub.server.nuspec", "Ably.PubSub.Server" },
        };

        /// <summary>
        /// The door packages: the ones that must pin the core exactly.
        /// </summary>
        private static readonly string[] DoorNuspecs = { "ably.pubsub.device.nuspec", "ably.pubsub.server.nuspec" };

        /// <summary>
        /// Package ids that must never be a dependency of anything released from this branch.
        /// `ably.io` and the old push satellites are published from the 1.x maintenance branch;
        /// a 2.0 package that depended on one of them would put two Ably cores in one project.
        /// </summary>
        private static readonly string[] ForbiddenDependencyIds =
        {
            "ably.io", "io.ably", "ably.io.push.android", "ably.io.push.ios",
        };

        /// <summary>
        /// Gets the nuspec file names, as xunit theory data.
        /// </summary>
        public static IEnumerable<object[]> Nuspecs => PackageSet.Keys.Select(k => new object[] { k });

        [Fact]
        [Trait("spec", "packaging")]
        public void NugetDirectory_ContainsExactlyTheLockstepPackageSet()
        {
            var found = Directory.GetFiles(NuspecDirectory(), "*.nuspec")
                .Select(Path.GetFileName)
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            // Exact, not "contains": a fourth package, or a resurrected nuget/io.ably.nuspec,
            // means something is being published that this lockstep release was not designed for.
            found.Should().BeEquivalentTo(
                PackageSet.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase),
                "the release publishes exactly this set of packages at one version");
        }

        [Theory]
        [MemberData(nameof(Nuspecs))]
        [Trait("spec", "packaging")]
        public void Nuspec_DeclaresTheExpectedPackageId(string nuspecName)
        {
            Child(Metadata(nuspecName), "id").Should().NotBeNull();
            Child(Metadata(nuspecName), "id").Value.Trim()
                .Should().Be(PackageSet[nuspecName], "a published package id cannot be renamed, only replaced");
        }

        [Theory]
        [MemberData(nameof(Nuspecs))]
        [Trait("spec", "packaging")]
        public void Nuspec_TakesItsVersionFromTheReleaseInput(string nuspecName)
        {
            // A hard-coded version here would let one package of the set ship at a different
            // version from its siblings, which is exactly what lockstep exists to prevent.
            Child(Metadata(nuspecName), "version").Value.Trim()
                .Should().Be("$version$", "the pack version comes from the release input alone");
        }

        [Theory]
        [MemberData(nameof(Nuspecs))]
        [Trait("spec", "packaging")]
        public void Nuspec_DoesNotDependOnThe1xPackages(string nuspecName)
        {
            var ids = Dependencies(nuspecName).Select(d => d.Id).ToArray();

            foreach (var forbidden in ForbiddenDependencyIds)
            {
                ids.Should().NotContain(
                    id => string.Equals(id, forbidden, StringComparison.OrdinalIgnoreCase),
                    $"{forbidden} is a 1.x package published from the maintenance branch");
            }
        }

        [Theory]
        [MemberData(nameof(Nuspecs))]
        [Trait("spec", "packaging")]
        public void DoorNuspec_PinsTheCoreExactly_InEveryTargetFrameworkGroup(string nuspecName)
        {
            var corePins = Dependencies(nuspecName)
                .Where(d => string.Equals(d.Id, CorePackageId, StringComparison.OrdinalIgnoreCase))
                .ToArray();

            if (!DoorNuspecs.Contains(nuspecName, StringComparer.OrdinalIgnoreCase))
            {
                corePins.Should().BeEmpty("the core package cannot depend on itself");
                return;
            }

            var groupCount = Metadata(nuspecName)
                .Descendants()
                .Count(e => e.Name.LocalName == "group");

            groupCount.Should().BeGreaterThan(0);

            // One pin per group. A group without one leaves that target framework free to
            // resolve whatever core version it likes.
            corePins.Should().HaveCount(
                groupCount,
                $"every one of the {groupCount} dependency groups must pin {CorePackageId}");

            foreach (var pin in corePins)
            {
                // The square brackets are the whole point: "2.0.0" is a minimum version, so a
                // consumer could silently resolve a newer core than this door was built and
                // tested against, and two doors could pull two different cores into one project.
                pin.Version.Should().Be(
                    "[$version$]",
                    $"{CorePackageId} must be pinned to the exact version being released");
            }
        }

        [Fact]
        [Trait("spec", "packaging")]
        public void TheTwoVersionFiles_Agree()
        {
            var assemblyInfo = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "CommonAssemblyInfo.cs"));
            var unityVersion = File.ReadAllText(
                Path.Combine(RepositoryRoot(), "unity", "Assets", "Ably", "version.txt")).Trim();

            var versions = new[] { "AssemblyVersion", "AssemblyFileVersion", "AssemblyInformationalVersion" }
                .Select(attribute => Regex.Match(assemblyInfo, attribute + @"\(""([^""]*)""\)"))
                .ToArray();

            versions.Should().OnlyContain(m => m.Success, "src/CommonAssemblyInfo.cs declares all three attributes");

            foreach (var match in versions)
            {
                match.Groups[1].Value.Should().Be(
                    unityVersion,
                    "src/CommonAssemblyInfo.cs and unity/Assets/Ably/version.txt are bumped together; the "
                    + ".unitypackage ships from the same run as the NuGet packages");
            }
        }

        private static XElement Child(XElement parent, string localName)
        {
            return parent.Elements().SingleOrDefault(e => e.Name.LocalName == localName);
        }

        private static XElement Metadata(string nuspecName)
        {
            var document = XDocument.Load(Path.Combine(NuspecDirectory(), nuspecName));

            // Matched by local name so the assertions do not depend on the nuspec schema
            // namespace, which nuget has changed before.
            return document.Root.Elements().Single(e => e.Name.LocalName == "metadata");
        }

        private static IEnumerable<(string Id, string Version)> Dependencies(string nuspecName)
        {
            return Metadata(nuspecName)
                .Descendants()
                .Where(e => e.Name.LocalName == "dependency")
                .Select(e => (
                    Id: (string)e.Attribute("id"),
                    Version: (string)e.Attribute("version")))
                .ToArray();
        }

        private static string NuspecDirectory() => Path.Combine(RepositoryRoot(), "nuget");

        /// <summary>
        /// Walks up from the test assembly to the repository root. The test assembly lives under
        /// src/Ably.PubSub.Tests.DotNET/bin/&lt;configuration&gt;/&lt;tfm&gt;/, but the depth differs
        /// between a local run and CI, so the root is found by looking for what identifies it
        /// rather than by counting directories.
        /// </summary>
        private static string RepositoryRoot()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);

            while (directory != null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "nuget", "ably.pubsub.core.nuspec")))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }

            throw new DirectoryNotFoundException(
                $"Could not find the repository root (a directory containing nuget/ably.pubsub.core.nuspec) "
                + $"above {AppContext.BaseDirectory}.");
        }
    }
}

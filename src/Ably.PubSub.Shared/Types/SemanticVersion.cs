using System;

namespace IO.Ably.Types
{
    /// <summary>
    /// SemanticVersion is a simple representation of a semantic version as defined by https://semver.org/ .
    /// </summary>
    public class SemanticVersion
    {
        private readonly int _major;
        private readonly int _minor;
        private readonly int _patch;

        /// <summary>
        /// Default construct a SemanticVersion instance setting 'Major', 'Minor' and 'Patch' to 0.
        /// </summary>
        public SemanticVersion()
        {
            _major = 0;
            _minor = 0;
            _patch = 0;
        }

        /// <summary>
        /// Construct a SemanticVersion instance using the specified 'major', 'minor' and 'patch'.
        /// </summary>
        /// <param name="major">The Major component of the semantic version.</param>
        /// <param name="minor">The Minor component of the semantic version.</param>
        /// <param name="patch">The Patch component of the semantic version.</param>
        public SemanticVersion(int major, int minor, int patch)
        {
            _major = major;
            _minor = minor;
            _patch = patch;
        }

        /// <summary>
        /// Construct a SemanticVersion instance from a standard .NET 'Version'.  In doing this
        /// 'Major' and 'Minor' map directly, 'SemanticVersion.Patch' maps to 'Version.Build'
        /// and 'Version.Revision' is discarded.
        /// </summary>
        /// <param name="version">The 'Version' to translate.</param>
        public SemanticVersion(Version version)
        {
            _major = version.Major;
            _minor = version.Minor;
            _patch = version.Build;
        }

        /// <summary>
        /// The 'Major' value set at construction.
        /// </summary>
        public int Major => _major;

        /// <summary>
        /// The 'Minor' value set at construction.
        /// </summary>
        public int Minor => _minor;

        /// <summary>
        /// The 'Patch' value set at construction.
        /// </summary>
        public int Patch => _patch;

        /// <summary>
        /// Return the string representation of this type conforming to that defined by https://semver.org/ .
        /// </summary>
        /// <returns>The string representation.</returns>
        public override string ToString()
        {
            return $"{_major}.{_minor}.{_patch}";
        }
    }
}

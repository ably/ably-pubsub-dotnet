///////////////////////////////////////////////////////////////////////////////
// RELEASE CHECKS AND NOTES
///////////////////////////////////////////////////////////////////////////////
// The checks and the release notes for .github/workflows/release.yml. Standalone:
// not loaded by build.cake. Usage and targets: "Release targets" in
// cake-build/README.md. Arguments go after a -- separator (Cake reserves --version):
//
//   dotnet cake cake-build/release.cake -- --target=Release.Preflight --version=2.0.1

using System.IO.Compression;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

///////////////////////////////////////////////////////////////////////////////
// ARGUMENTS
///////////////////////////////////////////////////////////////////////////////

var target = Argument("target", "Release.Preflight");
var version = Argument("version", "");
var dryRun = Argument("dryRun", false);

// The repository root: Cake's working directory is the script's, cake-build/.
var root = MakeAbsolute(Directory("../"));

var changelogFile = RepoPath(Argument("changelog", "CHANGELOG.md"));
var unityPackageFile = RepoPath(Argument("unityPackage", $"ably.pubsub.{version}.unitypackage"));
var notesFile = RepoPath(Argument("output", "release-notes.md"));

// The repository whose tags and releases P7 checks: the one the workflow runs in.
// A nonexistent repository answers 404, which reads as "absent", so a local run
// against a fork must pass the right --repository.
var repository = Argument("repository", EnvironmentVariable("GITHUB_REPOSITORY") ?? "ably/ably-pubsub-dotnet");

// Overridable only to test the failure handling against another host.
var nugetFlatContainerUrl = Argument("nugetFlatContainerUrl", "https://api.nuget.org/v3-flatcontainer");
var githubApiUrl = Argument("githubApiUrl", "https://api.github.com");

// The packages released together, Core first (the order they are pushed in).
var releasePackageIds = new[] { "Ably.PubSub.Core", "Ably.PubSub.Device", "Ably.PubSub.Server" };

// 15 s per attempt keeps the worst case (5 GETs x 4 attempts) inside the prechecks
// job's 10-minute timeout, so it reports its problems instead of being cancelled.
var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
http.DefaultRequestHeaders.UserAgent.ParseAdd("ably-pubsub-dotnet-release-cake");

///////////////////////////////////////////////////////////////////////////////
// HELPERS
///////////////////////////////////////////////////////////////////////////////

public FilePath RepoPath(string path)
{
    FilePath candidate = path;
    return candidate.IsRelative ? root.CombineWithFilePath(candidate) : candidate;
}

public string RequireVersionArgument()
{
    if (string.IsNullOrWhiteSpace(version))
    {
        throw new Exception(
            "No --version was supplied. Pass it after a -- separator, e.g. " +
            "dotnet cake cake-build/release.cake -- --target=Release.Preflight --version=2.0.1 " +
            "(Cake reserves --version for itself).");
    }

    return version;
}

public bool IsOnGitHubActions()
{
    return BuildSystem.GitHubActions.IsRunningOnGitHubActions;
}

// An annotation in Actions (Cake's log colours its lines, which hides a ::warning::
// prefix from GitHub), a plain warning otherwise.
public void ReportWarning(string message)
{
    if (IsOnGitHubActions())
    {
        BuildSystem.GitHubActions.Commands.Warning(message);
    }
    else
    {
        Warning("{0}", message);
    }
}

// Annotates every error in Actions, then fails the task once with all of them.
public void FailIfAny(List<string> errors, string what)
{
    if (errors.Count == 0)
    {
        return;
    }

    if (IsOnGitHubActions())
    {
        foreach (var error in errors)
        {
            BuildSystem.GitHubActions.Commands.Error(error);
        }
    }

    throw new Exception(
        $"{what} failed with {errors.Count} problem(s):\n\n  - " + string.Join("\n\n  - ", errors) + "\n");
}

///////////////////////////////////////////////////////////////////////////////
// VERSION AND CHANGELOG
///////////////////////////////////////////////////////////////////////////////

// SemVer 2.0 without build metadata, which nuget.org ignores: no leading v, no +build.
public bool IsReleaseVersion(string candidate)
{
    return Regex.IsMatch(
        candidate,
        @"\A(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(-[0-9A-Za-z-]+(\.[0-9A-Za-z-]+)*)?\z");
}

public string[] ReadChangelog()
{
    if (!FileExists(changelogFile))
    {
        throw new Exception($"{changelogFile.FullPath} not found.");
    }

    // ReadAllLines also drops a trailing \r, in case the file is checked out with CRLF.
    return System.IO.File.ReadAllLines(changelogFile.FullPath);
}

// The lines under the heading at headingIndex, up to the next "## [" heading, without
// the "[Full Changelog]" line. Leading and trailing blank lines are removed, and blank
// lines in between are emptied.
public List<string> ChangelogSection(string[] lines, int headingIndex)
{
    var section = lines
        .Skip(headingIndex + 1)
        .TakeWhile(line => !line.StartsWith("## ["))
        .Where(line => !line.StartsWith("[Full Changelog]"))
        .Select(line => string.IsNullOrWhiteSpace(line) ? "" : line)
        .SkipWhile(line => line.Length == 0)
        .ToList();

    while (section.Count > 0 && section[section.Count - 1].Length == 0)
    {
        section.RemoveAt(section.Count - 1);
    }

    return section;
}

// P3. The top entry must read "## [2.0.0](https://github.com/ably/ably-pubsub-dotnet/tree/2.0.0) (2026-11-03)",
// so a placeholder date (TBD, Unreleased) fails, and its section becomes the release notes.
public void CheckChangelogTopEntry(string releaseVersion, List<string> errors)
{
    var lines = ReadChangelog();
    var headingIndex = Array.FindIndex(lines, line => line.StartsWith("## ["));
    var heading = headingIndex < 0 ? "(none)" : lines[headingIndex];
    var expected = $"## [{releaseVersion}](https://github.com/ably/ably-pubsub-dotnet/tree/{releaseVersion}) (yyyy-mm-dd)";

    var match = Regex.Match(
        heading,
        @"\A## \[([^\]]+)\]\(https://github\.com/ably/ably-pubsub-dotnet/tree/([^)]+)\) \(([0-9]{4}-[0-9]{2}-[0-9]{2})\)\z");
    if (!match.Success || match.Groups[1].Value != releaseVersion || match.Groups[2].Value != releaseVersion)
    {
        errors.Add($"The top CHANGELOG.md entry is '{heading}'. Expected '{expected}'. Add the {releaseVersion} entry in the release PR.");
        return;
    }

    var section = ChangelogSection(lines, headingIndex);
    if (section.Count == 0)
    {
        errors.Add($"The CHANGELOG.md {releaseVersion} entry has no content. It becomes the GitHub release notes.");
        return;
    }

    Information($"CHANGELOG.md: {heading} (non-blank lines: {section.Count(line => line.Length > 0)}).");
}

///////////////////////////////////////////////////////////////////////////////
// REMOTE STATE (fail closed: only a 404 means "absent")
///////////////////////////////////////////////////////////////////////////////

public class HttpResult
{
    // 0 when no response was received.
    public int Status { get; set; }
    public string Body { get; set; } = "";
    public string Failure { get; set; } = "";

    public string Describe(string url)
    {
        if (Status == 0)
        {
            return $"GET {url} failed: {Failure}";
        }

        var snippet = Body.Replace("\r", " ").Replace("\n", " ").Trim();
        if (snippet.Length > 200)
        {
            snippet = snippet.Substring(0, 200) + "...";
        }

        return $"GET {url} returned HTTP {Status}" + (snippet.Length > 0 ? $": {snippet}" : ".");
    }
}

// GET with up to 3 retries on a network error, a timeout, 429 or 5xx, as curl --retry does.
public HttpResult HttpGet(string url, bool gitHubApi)
{
    const int attempts = 4;
    var result = new HttpResult();
    for (var attempt = 1; attempt <= attempts; attempt++)
    {
        try
        {
            using (var request = new HttpRequestMessage(HttpMethod.Get, url))
            {
                if (gitHubApi)
                {
                    request.Headers.Accept.ParseAdd("application/vnd.github+json");
                    var token = EnvironmentVariable("GITHUB_TOKEN");
                    if (!string.IsNullOrEmpty(token))
                    {
                        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
                    }
                }

                using (var response = http.SendAsync(request).GetAwaiter().GetResult())
                {
                    result = new HttpResult
                    {
                        Status = (int)response.StatusCode,
                        Body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
                    };
                }
            }

            if (result.Status != 429 && result.Status < 500)
            {
                return result;
            }
        }
        catch (Exception e) when (e is HttpRequestException || e is TaskCanceledException)
        {
            result = new HttpResult { Failure = e.Message };
        }

        if (attempt < attempts)
        {
            System.Threading.Thread.Sleep(TimeSpan.FromSeconds(2 * attempt));
        }
    }

    return result;
}

// The root of a JSON body, or null if the body isn't a JSON object.
public JsonElement? ParseJsonObject(string body)
{
    try
    {
        using (var document = JsonDocument.Parse(body))
        {
            return document.RootElement.ValueKind == JsonValueKind.Object
                ? document.RootElement.Clone()
                : (JsonElement?)null;
        }
    }
    catch (JsonException)
    {
        return null;
    }
}

// P6. The flat container lists listed and unlisted versions, in lower case, and
// returns 404 for an ID with no versions.
public void CheckNotOnNuGet(string releaseVersion, List<string> released, List<string> errors)
{
    foreach (var packageId in releasePackageIds)
    {
        var url = $"{nugetFlatContainerUrl}/{packageId.ToLowerInvariant()}/index.json";
        var response = HttpGet(url, false);
        if (response.Status == 404)
        {
            Information($"{packageId}: no versions on nuget.org yet.");
            continue;
        }

        if (response.Status != 200)
        {
            errors.Add(response.Describe(url));
            continue;
        }

        var index = ParseJsonObject(response.Body);
        if (index == null
            || !index.Value.TryGetProperty("versions", out var versions)
            || versions.ValueKind != JsonValueKind.Array
            || versions.EnumerateArray().Any(v => v.ValueKind != JsonValueKind.String))
        {
            errors.Add($"GET {url} returned HTTP 200 with a body that isn't the expected {{\"versions\": [...]}} JSON.");
            continue;
        }

        if (versions.EnumerateArray().Any(v => v.GetString() == releaseVersion.ToLowerInvariant()))
        {
            released.Add($"{packageId} {releaseVersion} is already on nuget.org (listed or unlisted). NuGet versions can't be reused: bump the version.");
        }
        else
        {
            Information($"{packageId}: {releaseVersion} isn't on nuget.org.");
        }
    }
}

// P7, for one of the tag or the release: 404 means absent, a 200 whose `property` is
// `expected` means present, and anything else is an error.
public void CheckNotOnGitHub(string path, string property, string expected, string presentMessage, List<string> released, List<string> errors)
{
    var url = $"{githubApiUrl}/repos/{repository}/{path}";
    var response = HttpGet(url, true);
    if (response.Status == 404)
    {
        Information($"{url}: not found.");
        return;
    }

    if (response.Status != 200)
    {
        errors.Add(response.Describe(url));
        return;
    }

    var body = ParseJsonObject(response.Body);
    if (body != null
        && body.Value.TryGetProperty(property, out var value)
        && value.ValueKind == JsonValueKind.String
        && value.GetString() == expected)
    {
        released.Add(presentMessage);
        return;
    }

    errors.Add($"GET {url} returned HTTP 200 without the expected \"{property}\": \"{expected}\".");
}

///////////////////////////////////////////////////////////////////////////////
// UNITY PACKAGE
///////////////////////////////////////////////////////////////////////////////

// A .unitypackage is a gzipped tar of <guid>/ folders, each holding a "pathname" file
// (the path in the Unity project) and an "asset" file (its content). Returns the
// content of every asset by its Unity path. Read in memory with a minimal ustar reader
// (the packager writes plain ustar), because System.Formats.Tar needs .NET 7.
public Dictionary<string, byte[]> ReadUnityPackageAssets(FilePath unityPackage)
{
    const long maxEntrySize = 256L * 1024 * 1024;
    var pathnames = new Dictionary<string, string>();
    var contents = new Dictionary<string, byte[]>();
    var header = new byte[512];

    using (var file = System.IO.File.OpenRead(unityPackage.FullPath))
    using (var gzip = new GZipStream(file, CompressionMode.Decompress))
    {
        while (true)
        {
            if (ReadFully(gzip, header, header.Length) < header.Length)
            {
                throw new Exception("it ends without the tar end-of-archive marker (truncated?)");
            }

            if (header.All(b => b == 0))
            {
                break;
            }

            var name = ReadTarHeader(header, out var size, out var type);
            if (type == 'L' || type == 'K' || type == 'x' || type == 'g')
            {
                throw new Exception($"it uses a tar extension (type '{type}') that the Unity packager doesn't write");
            }

            if (size > maxEntrySize)
            {
                throw new Exception($"{name} is larger than {maxEntrySize} bytes");
            }

            // Only regular files named <guid>/pathname or <guid>/asset are read; the rest is skipped.
            var parts = name.TrimStart('.', '/').Split('/');
            var wanted = (type == '0' || type == '\0') && parts.Length == 2
                && (parts[1] == "pathname" || parts[1] == "asset");
            var data = new byte[wanted ? size : 0];
            if (wanted && ReadFully(gzip, data, data.Length) < data.Length)
            {
                throw new Exception($"{name} is truncated");
            }

            var rest = (size + 511) / 512 * 512 - data.Length;
            if (SkipFully(gzip, rest) < rest)
            {
                throw new Exception($"{name} is truncated");
            }

            if (wanted && (parts[1] == "pathname" ? pathnames.ContainsKey(parts[0]) : contents.ContainsKey(parts[0])))
            {
                throw new Exception($"it holds {name} more than once");
            }

            if (wanted && parts[1] == "pathname")
            {
                // The path is the first line; some Unity versions add a second one.
                pathnames[parts[0]] = Encoding.UTF8.GetString(data).Split('\n')[0].TrimEnd('\r');
            }
            else if (wanted)
            {
                contents[parts[0]] = data;
            }
        }
    }

    var assets = new Dictionary<string, byte[]>();
    foreach (var pathname in pathnames.Where(p => contents.ContainsKey(p.Key)))
    {
        if (assets.ContainsKey(pathname.Value))
        {
            throw new Exception($"it holds {pathname.Value} more than once");
        }

        assets[pathname.Value] = contents[pathname.Key];
    }

    return assets;
}

// The entry name of a ustar header, with its size and type flag. Fails on a bad checksum.
public string ReadTarHeader(byte[] header, out long size, out char type)
{
    // The checksum is the sum of all header bytes, with its own 8 bytes counted as spaces.
    long sum = 0;
    for (var i = 0; i < header.Length; i++)
    {
        sum += i >= 148 && i < 156 ? ' ' : header[i];
    }

    if (sum != ReadTarOctal(header, 148, 8))
    {
        throw new Exception("a tar header has a bad checksum");
    }

    size = ReadTarOctal(header, 124, 12);
    type = (char)header[156];
    var name = ReadTarString(header, 0, 100);
    // Only POSIX ustar ("ustar\0") has a name prefix; GNU tar keeps timestamps there.
    var prefix = Encoding.ASCII.GetString(header, 257, 6) == "ustar\0" ? ReadTarString(header, 345, 155) : "";
    return prefix.Length > 0 ? $"{prefix}/{name}" : name;
}

public string ReadTarString(byte[] header, int offset, int length)
{
    var end = Array.IndexOf(header, (byte)0, offset, length);
    return Encoding.UTF8.GetString(header, offset, (end < 0 ? offset + length : end) - offset);
}

public long ReadTarOctal(byte[] header, int offset, int length)
{
    var text = Encoding.Latin1.GetString(header, offset, length).Trim(' ', '\0');
    if (text.Length == 0 || text.Any(c => c < '0' || c > '7'))
    {
        throw new Exception($"a tar header has a malformed number field ('{Printable(text)}')");
    }

    return Convert.ToInt64(text, 8);
}

// Text from a malformed header, with control and non-ASCII characters escaped.
public string Printable(string text)
{
    return string.Concat(text.Select(c => c >= ' ' && c < 127 ? c.ToString() : $"\\x{(int)c:x2}"));
}

// Reads until count bytes or the end of the stream; returns the number read.
public int ReadFully(System.IO.Stream stream, byte[] buffer, int count)
{
    var total = 0;
    int read;
    while (total < count && (read = stream.Read(buffer, total, count - total)) > 0)
    {
        total += read;
    }

    return total;
}

public long SkipFully(System.IO.Stream stream, long count)
{
    var buffer = new byte[81920];
    long total = 0;
    while (total < count)
    {
        var read = ReadFully(stream, buffer, (int)Math.Min(buffer.Length, count - total));
        if (read == 0)
        {
            break;
        }

        total += read;
    }

    return total;
}

///////////////////////////////////////////////////////////////////////////////
// TASKS
///////////////////////////////////////////////////////////////////////////////

Setup(ctx =>
{
    var dryRunNote = target == "Release.Preflight" ? $", dry run: {dryRun}" : "";
    Information($"Release target: {target}, version: {RequireVersionArgument()}{dryRunNote}");
});

Task("Release.Preflight")
    .Description("Check the version, the top CHANGELOG.md entry, and that the version isn't on nuget.org, tagged or released")
    .Does(() =>
{
    var releaseVersion = RequireVersionArgument();
    var errors = new List<string>();
    // Problems that only mean "already released": warnings on a dry run, errors otherwise.
    var released = new List<string>();

    // P2.
    var isReleaseVersion = IsReleaseVersion(releaseVersion);
    if (!isReleaseVersion)
    {
        errors.Add($"'{releaseVersion}' is not MAJOR.MINOR.PATCH[-prerelease] (no leading v, no +build).");
    }

    // P3.
    CheckChangelogTopEntry(releaseVersion, errors);

    // P6 and P7. Skipped for a version that isn't SemVer: it can't be released anyway.
    if (isReleaseVersion)
    {
        CheckNotOnNuGet(releaseVersion, released, errors);
        CheckNotOnGitHub($"git/ref/tags/{releaseVersion}", "ref", $"refs/tags/{releaseVersion}", $"Tag {releaseVersion} already exists.", released, errors);
        CheckNotOnGitHub($"releases/tags/{releaseVersion}", "tag_name", releaseVersion, $"GitHub release {releaseVersion} already exists.", released, errors);
    }

    if (dryRun)
    {
        released.ForEach(ReportWarning);
    }
    else
    {
        errors.AddRange(released);
    }

    FailIfAny(errors, $"Release pre-flight for {releaseVersion}");
    Information($"Pre-flight passed for {releaseVersion} (dry run: {dryRun}, warnings: {(dryRun ? released.Count : 0)}).");
});

// K8. The Unity package has no registry and no install test, and package.yml only
// checks that the file exists.
Task("Release.VerifyUnityPackage")
    .Description("Check the .unitypackage holds the Unity plugin and version.txt at --version")
    .Does(() =>
{
    var releaseVersion = RequireVersionArgument();
    var name = unityPackageFile.GetFilename().FullPath;
    if (!FileExists(unityPackageFile))
    {
        throw new Exception($"{unityPackageFile.FullPath} not found.");
    }

    Dictionary<string, byte[]> assets;
    try
    {
        assets = ReadUnityPackageAssets(unityPackageFile);
    }
    catch (Exception e)
    {
        throw new Exception($"{name} isn't a readable .unitypackage (a gzipped tar of <guid>/pathname and asset files): {e.Message}");
    }

    var errors = new List<string>();
    const string pluginPath = "Assets/Ably/Plugins/Ably.PubSub.Device.dll";
    const string versionPath = "Assets/Ably/version.txt";

    if (!assets.TryGetValue(pluginPath, out var plugin) || plugin.Length == 0)
    {
        errors.Add($"{name} has no {pluginPath}, or it is empty.");
    }

    if (!assets.TryGetValue(versionPath, out var versionFile))
    {
        errors.Add($"{name} has no {versionPath}.");
    }
    else
    {
        var packedVersion = System.Text.Encoding.UTF8.GetString(versionFile).Trim();
        if (packedVersion != releaseVersion)
        {
            errors.Add($"{name} carries {versionPath} '{packedVersion}', expected '{releaseVersion}'.");
        }
    }

    FailIfAny(errors, $"Unity package check for {releaseVersion}");
    Information($"{name} holds {pluginPath} and {versionPath} {releaseVersion}.");
});

Task("Release.Notes")
    .Description("Write the CHANGELOG.md section for --version to --output, for the GitHub release")
    .Does(() =>
{
    var releaseVersion = RequireVersionArgument();
    var lines = ReadChangelog();
    var headingIndex = Array.FindIndex(lines, line => line.StartsWith($"## [{releaseVersion}]"));
    var notes = headingIndex < 0 ? new List<string>() : ChangelogSection(lines, headingIndex);
    if (notes.Count == 0)
    {
        throw new Exception($"CHANGELOG.md has no {releaseVersion} section, or it is empty.");
    }

    EnsureDirectoryExists(notesFile.GetDirectory());
    System.IO.File.WriteAllText(notesFile.FullPath, string.Join("\n", notes) + "\n");
    Information($"Wrote the {releaseVersion} release notes ({notes.Count} lines) to {notesFile.FullPath}.");
});

RunTarget(target);

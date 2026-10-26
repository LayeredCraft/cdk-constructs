using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Amazon.CDK;
using Amazon.CDK.Assertions;
using AwesomeAssertions;
using LayeredCraft.Cdk.Constructs.Testing;

namespace LayeredCraft.Cdk.Constructs.Tests.TestKit.Golden;

/// <summary>
/// Golden-template harness used to pin the complete synthesized CloudFormation template of the
/// existing constructs. It exists so an <c>Amazon.CDK.Lib</c> bump (or any accidental construct
/// change) cannot silently alter the deployed shape of a construct (design §12.4).
/// </summary>
/// <remarks>
/// <para>
/// The harness compares the <em>complete</em> template produced by
/// <see cref="Template.FromStack(Stack).ToJSON()"/>. Only the following are normalized, and nothing
/// else is stripped:
/// </para>
/// <list type="bullet">
///   <item><description>the <c>BootstrapVersion</c> parameter and the <c>CheckBootstrapVersion</c> rule
///   that CDK adds for the bootstrap contract;</description></item>
///   <item><description>the <c>AWS::CDK::Metadata</c> resource;</description></item>
///   <item><description>per-resource <c>Metadata</c> (contains <c>aws:cdk:path</c> and
///   <c>aws:asset:path</c>, which embed local, machine-specific paths);</description></item>
///   <item><description>asset content hashes / asset S3 keys — every 64-character lowercase hex token
///   (SHA-256 asset fingerprint) is replaced with <c>&lt;ASSET-HASH&gt;</c> so the comparison is stable
///   across machines and CDK re-hashing while still catching a changed asset set.</description></item>
/// </list>
/// <para>
/// Regeneration is only performed when the <see cref="UpdateEnvironmentVariable"/> environment
/// variable is set to a truthy value. In the default mode any diff fails the test with a readable
/// line diff, and the golden file is never overwritten.
/// </para>
/// </remarks>
public static class GoldenTemplate
{
    /// <summary>When set to a truthy value, golden files are (re)written instead of compared.</summary>
    public const string UpdateEnvironmentVariable = "UPDATE_GOLDEN_TEMPLATES";

    private const string AssetHashPlaceholder = "<ASSET-HASH>";

    private static readonly string OutputGoldenDirectory = CdkTestHelper.GetTestAssetPath("GoldenTemplates");

    /// <summary>
    /// The checked-in golden directory in the repository source tree, discovered by walking up from
    /// the test output directory to the project directory (the one containing the <c>.csproj</c>).
    /// Regeneration always writes here so the golden files that are committed are the ones updated;
    /// reading prefers this location so a stale build-output copy can never mask a change.
    /// </summary>
    private static readonly string SourceGoldenDirectory = ResolveSourceGoldenDirectory();

    private static readonly Regex AssetHashRegex = new("[0-9a-f]{64}", RegexOptions.Compiled);

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>
    /// Normalizes the stack's synthesized template and either writes it to
    /// <c>GoldenTemplates/<paramref name="goldenFileName"/></c> (regeneration mode) or asserts it is
    /// byte-for-byte identical to the checked-in golden file (default mode).
    /// </summary>
    /// <param name="stack">The stack to synthesize. Constructs must already have been added.</param>
    /// <param name="goldenFileName">File name, including the <c>.json</c> extension.</param>
    public static void AssertMatches(Stack stack, string goldenFileName)
    {
        var actual = Normalize(Template.FromStack(stack).ToJSON());

        if (IsUpdateRequested())
        {
            Directory.CreateDirectory(SourceGoldenDirectory);
            File.WriteAllText(Path.Join(SourceGoldenDirectory, goldenFileName), actual);
            throw new InvalidOperationException(
                $"Golden template '{goldenFileName}' was regenerated because {UpdateEnvironmentVariable} is set. " +
                "Unset the environment variable and re-run to verify.");
        }

        var path = ResolveReadPath(goldenFileName);

        var expected = File.ReadAllText(path);

        if (!string.Equals(NormalizeLineEndings(expected), NormalizeLineEndings(actual), StringComparison.Ordinal))
        {
            Assert.Fail(BuildDiffMessage(goldenFileName, expected, actual));
        }
    }

    private static string ResolveReadPath(string goldenFileName)
    {
        var sourcePath = Path.Join(SourceGoldenDirectory, goldenFileName);

        if (File.Exists(sourcePath))
        {
            return sourcePath;
        }

        var outputPath = Path.Join(OutputGoldenDirectory, goldenFileName);

        if (File.Exists(outputPath))
        {
            return outputPath;
        }

        throw new InvalidOperationException(
            $"Golden template '{goldenFileName}' is missing. Run the tests once with {UpdateEnvironmentVariable}=1 to create it.");
    }

    private static string ResolveSourceGoldenDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (directory.EnumerateFiles("*.csproj").Any())
            {
                return Path.Join(directory.FullName, "GoldenTemplates");
            }

            directory = directory.Parent;
        }

        return OutputGoldenDirectory;
    }

    private static string Normalize(IDictionary<string, object> template)
    {
        var root = (JsonObject)JsonSerializer.SerializeToNode(template, SerializerOptions)!;

        if (root["Parameters"] is JsonObject parameters)
        {
            parameters.Remove("BootstrapVersion");

            if (parameters.Count == 0)
            {
                root.Remove("Parameters");
            }
        }

        root.Remove("Rules");

        if (root["Resources"] is JsonObject resources)
        {
            foreach (var (logicalId, value) in resources.ToList())
            {
                if (value is not JsonObject resource)
                {
                    continue;
                }

                if (resource["Type"]?.GetValue<string>() == "AWS::CDK::Metadata")
                {
                    resources.Remove(logicalId);
                    continue;
                }

                resource.Remove("Metadata");
            }
        }

        var json = root.ToJsonString(SerializerOptions);

        return AssetHashRegex.Replace(json, AssetHashPlaceholder);
    }

    private static bool IsUpdateRequested()
    {
        var value = System.Environment.GetEnvironmentVariable(UpdateEnvironmentVariable);

        return !string.IsNullOrWhiteSpace(value) &&
               !string.Equals(value, "0", StringComparison.Ordinal) &&
               !string.Equals(value, "false", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeLineEndings(string value) => value.Replace("\r\n", "\n");

    private static string BuildDiffMessage(string goldenFileName, string expected, string actual)
    {
        var expectedLines = NormalizeLineEndings(expected).Split('\n');
        var actualLines = NormalizeLineEndings(actual).Split('\n');

        var message = new System.Text.StringBuilder();
        message.AppendLine($"Golden template '{goldenFileName}' differs from the synthesized template.");
        message.AppendLine();
        message.AppendLine("--- expected (golden)");
        message.AppendLine("+++ actual (synthesized)");
        message.AppendLine("@@ first differing lines @@");

        var shown = 0;
        var max = Math.Max(expectedLines.Length, actualLines.Length);

        for (var i = 0; i < max && shown < 40; i++)
        {
            var expectedLine = i < expectedLines.Length ? expectedLines[i] : "<missing>";
            var actualLine = i < actualLines.Length ? actualLines[i] : "<missing>";

            if (!string.Equals(expectedLine, actualLine, StringComparison.Ordinal))
            {
                message.AppendLine($"- {expectedLine}");
                message.AppendLine($"+ {actualLine}");
                shown++;
            }
        }

        return message.ToString();
    }
}

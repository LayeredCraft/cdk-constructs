using System.Security.Cryptography;
using System.Text;
using Amazon.CDK;

namespace LayeredCraft.Cdk.Constructs.Extensions;

/// <summary>
/// Extension methods for AWS CDK Stack to provide additional functionality.
/// </summary>
public static class StackExtensions
{
    /// <summary>
    /// Creates a CloudFormation export name following a consistent naming convention.
    /// Generates names in the format: {stack-name}-{id}-{qualifier}.
    /// If the resulting name exceeds 256 characters, it will be truncated and a hash suffix added.
    /// </summary>
    /// <param name="stack">The CDK stack instance</param>
    /// <param name="id">The construct ID</param>
    /// <param name="qualifier">The qualifier for the export (e.g., "arn", "name", "gsi-0")</param>
    /// <returns>A valid CloudFormation export name</returns>
    public static string CreateExportName(this Stack stack, string id, string qualifier)
    {
        var stackName = stack.StackName.ToLowerInvariant();
        var normalizedId = id.ToLowerInvariant();
        var normalizedQualifier = qualifier.ToLowerInvariant();

        var exportName = $"{stackName}-{normalizedId}-{normalizedQualifier}";

        if (exportName.Length <= 256) return exportName;
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(exportName))).Substring(0, 8).ToLowerInvariant();
        var maxBaseLength = 256 - hash.Length - 1;
        exportName = $"{exportName[..Math.Min(maxBaseLength, exportName.Length)]}-{hash}";

        return exportName;
    }
}
using Constructs;

namespace LayeredCraft.Cdk.Constructs.Models;

/// <summary>
/// The resolved S3 binding a Web Function revision needs, together with the policy change the revision must wait
/// for before it can read the artifact.
/// </summary>
internal sealed class WebFunctionCodeConfig
{
    internal WebFunctionCodeConfig(string bucket, string key, string? versionId, IDependable? policyDependable)
    {
        Bucket = bucket;
        Key = key;
        VersionId = versionId;
        PolicyDependable = policyDependable;
    }

    internal string Bucket { get; }

    internal string Key { get; }

    internal string? VersionId { get; }

    internal IDependable? PolicyDependable { get; }
}

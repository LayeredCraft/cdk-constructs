namespace LayeredCraft.Cdk.Constructs.Models;

/// <summary>
/// Options for <see cref="WebFunctionCode.FromBucket"/>.
/// </summary>
public sealed record WebFunctionCodeFromBucketOptions
{
    /// <summary>
    /// Pins the revision to this S3 object version. When null, Lambda pins the latest version at revision
    /// creation; later overwrites of the key do not affect existing revisions but do affect the next one
    /// CloudFormation creates.
    /// </summary>
    public string? ObjectVersion { get; init; }

    /// <summary>Who maintains the <c>lambda.amazonaws.com</c> read statement on the bucket.</summary>
    public WebFunctionCodeBucketPolicy BucketPolicy { get; init; } = WebFunctionCodeBucketPolicy.Automatic;
}

using Amazon.CDK;
using Amazon.CDK.AWS.S3;
using Constructs;

namespace WebFunctionSandbox;

/// <summary>
/// Owns the versioned SSE-S3 artifact bucket that the app stack consumes with the default
/// <see cref="LayeredCraft.Cdk.Constructs.Models.WebFunctionCodeBucketPolicy.Automatic"/> policy. It is a
/// separate stack so the scripts can deploy it and upload the artifact <em>before</em> the revision is created.
/// </summary>
internal sealed class ArtifactsStack : Stack
{
    public ArtifactsStack(
        Construct scope,
        string id,
        IStackProps? props,
        SandboxConfiguration configuration,
        SandboxNaming naming)
        : base(scope, id, props)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(naming);

        naming.ApplyTags(this);

        // RemovalPolicy.DESTROY and no AutoDeleteObjects: the sandbox destroy script empties every object version
        // before deleting the stack, and a plain `cdk destroy` that skips that step must fail rather than silently
        // delete data. Versioning is required by the Automatic bucket policy check.
        ArtifactsBucket = new Bucket(this, "ArtifactsBucket", new BucketProps
        {
            BucketName = naming.ArtifactsBucketName,
            Versioned = true,
            Encryption = BucketEncryption.S3_MANAGED,
            BlockPublicAccess = BlockPublicAccess.BLOCK_ALL,
            EnforceSSL = true,
            RemovalPolicy = RemovalPolicy.DESTROY,
            AutoDeleteObjects = false,
        });

        _ = new CfnOutput(this, "ArtifactsBucketName", new CfnOutputProps
        {
            Value = ArtifactsBucket.BucketName,
            Description = "Versioned S3 bucket holding sandbox artifacts (owned bucket, Automatic read policy).",
            ExportName = $"{naming.Prefix}-artifacts-bucket-name",
        });
    }

    public IBucket ArtifactsBucket { get; }
}
using Amazon.CDK;
using Amazon.CDK.AWS.CertificateManager;
using Amazon.CDK.AWS.CloudFront;
using Amazon.CDK.AWS.Route53;
using Amazon.CDK.AWS.S3.Deployment;

namespace LayeredCraft.Cdk.Constructs.Models;

/// <summary>
/// Interface defining configuration properties for <see cref="LayeredCraft.Cdk.Constructs.PrivateStaticAssetsConstruct"/>.
/// Describes immutable, versioned web assets stored in a private S3 bucket and served through a CloudFront
/// distribution that reaches the bucket with an origin access control.
/// </summary>
public interface IPrivateStaticAssetsConstructProps
{
    /// <summary>
    /// Path to the asset directory that is deployed to the bucket (a file or folder passed to
    /// <c>Source.Asset</c>).
    /// </summary>
    string AssetPath { get; set; }

    /// <summary>
    /// Optional key prefix in the destination bucket for the deployed assets. Recommended so a deployment
    /// only ever manages its own objects. Defaults to <c>null</c> (the bucket root).
    /// </summary>
    string? DestinationKeyPrefix { get; set; }

    /// <summary>
    /// Whether objects in the destination bucket that are not in the asset are deleted during deployment.
    /// Defaults to <c>false</c>.
    /// </summary>
    bool Prune { get; set; }

    /// <summary>
    /// Whether the deployed objects are retained when the deployment resource is deleted or replaced.
    /// Defaults to <c>true</c>.
    /// </summary>
    bool RetainOnDelete { get; set; }

    /// <summary>
    /// <c>Cache-Control</c> metadata applied to every deployed object. Defaults to none.
    /// </summary>
    CacheControl[] CacheControl { get; set; }

    /// <summary>
    /// Optional CloudFront invalidation paths. When set (a non-empty array), the distribution is invalidated
    /// for those paths after deployment; when omitted, no invalidation occurs. Defaults to <c>null</c>.
    /// </summary>
    string[]? InvalidationPaths { get; set; }

    /// <summary>
    /// Removal policy for the bucket. Defaults to <see cref="Amazon.CDK.RemovalPolicy.RETAIN"/>.
    /// </summary>
    RemovalPolicy RemovalPolicy { get; set; }

    /// <summary>
    /// Viewer protocol policy for the default behavior. Defaults to
    /// <see cref="Amazon.CDK.AWS.CloudFront.ViewerProtocolPolicy.HTTPS_ONLY"/>.
    /// </summary>
    ViewerProtocolPolicy ViewerProtocolPolicy { get; set; }

    /// <summary>
    /// Cache policy for the default behavior. Defaults to
    /// <see cref="Amazon.CDK.AWS.CloudFront.CachePolicy.CACHING_OPTIMIZED"/>.
    /// </summary>
    ICachePolicy CachePolicy { get; set; }

    /// <summary>Optional comment for the CloudFront distribution. Defaults to none.</summary>
    string? Comment { get; set; }

    /// <summary>
    /// Whether to attach a viewer-request CloudFront Function that rewrites extensionless paths to
    /// <c>/index.html</c> for single-page applications. Defaults to <c>false</c>.
    /// </summary>
    bool SpaRouting { get; set; }

    /// <summary>
    /// Custom error responses for the distribution. Defaults to none.
    /// </summary>
    IErrorResponse[] ErrorResponses { get; set; }

    /// <summary>Optional default root object for the distribution. Defaults to <c>null</c>.</summary>
    string? DefaultRootObject { get; set; }

    /// <summary>
    /// Optional custom DNS configuration. When omitted, the distribution's CloudFront domain name is used
    /// and no certificate or DNS records are created. Defaults to <c>null</c>.
    /// </summary>
    PrivateStaticAssetsDomainOptions? Domain { get; set; }
}

/// <summary>
/// Implementation record for <see cref="IPrivateStaticAssetsConstructProps"/>. Required members use
/// <c>required</c>; optional members carry the construct's documented defaults.
/// </summary>
public record PrivateStaticAssetsConstructProps : IPrivateStaticAssetsConstructProps
{
    /// <inheritdoc />
    public required string AssetPath { get; set; }

    /// <inheritdoc />
    public string? DestinationKeyPrefix { get; set; }

    /// <inheritdoc />
    public bool Prune { get; set; }

    /// <inheritdoc />
    public bool RetainOnDelete { get; set; } = true;

    /// <inheritdoc />
    public CacheControl[] CacheControl { get; set; } = [];

    /// <inheritdoc />
    public string[]? InvalidationPaths { get; set; }

    /// <inheritdoc />
    public RemovalPolicy RemovalPolicy { get; set; } = Amazon.CDK.RemovalPolicy.RETAIN;

    /// <inheritdoc />
    public ViewerProtocolPolicy ViewerProtocolPolicy { get; set; } = Amazon.CDK.AWS.CloudFront.ViewerProtocolPolicy.HTTPS_ONLY;

    /// <inheritdoc />
    public ICachePolicy CachePolicy { get; set; } = Amazon.CDK.AWS.CloudFront.CachePolicy.CACHING_OPTIMIZED;

    /// <inheritdoc />
    public string? Comment { get; set; }

    /// <inheritdoc />
    public bool SpaRouting { get; set; }

    /// <inheritdoc />
    public IErrorResponse[] ErrorResponses { get; set; } = [];

    /// <inheritdoc />
    public string? DefaultRootObject { get; set; }

    /// <inheritdoc />
    public PrivateStaticAssetsDomainOptions? Domain { get; set; }
}

/// <summary>
/// Optional custom DNS configuration for <see cref="PrivateStaticAssetsConstruct"/>. At least one domain name
/// is required. Provide a certificate, or a hosted zone (or hosted-zone name) so a DNS-validated certificate
/// can be created. A hosted zone and a hosted-zone name are mutually exclusive; when a zone is available,
/// A and AAAA alias records are created for every domain name.
/// </summary>
public record PrivateStaticAssetsDomainOptions
{
    /// <summary>The custom domain names to associate with the distribution. At least one is required.</summary>
    public required string[] DomainNames { get; set; }

    /// <summary>
    /// Optional certificate for the domain names. When omitted, a DNS-validated certificate is created using
    /// the hosted zone.
    /// </summary>
    public ICertificate? Certificate { get; set; }

    /// <summary>
    /// Optional hosted zone used for certificate validation and alias records. Mutually exclusive with
    /// <see cref="HostedZoneName"/>.
    /// </summary>
    public IHostedZone? HostedZone { get; set; }

    /// <summary>
    /// Optional hosted-zone name looked up with <c>HostedZone.FromLookup</c>. Mutually exclusive with
    /// <see cref="HostedZone"/>.
    /// </summary>
    public string? HostedZoneName { get; set; }
}

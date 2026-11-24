using Amazon.CDK;
using Amazon.CDK.AWS.CertificateManager;
using Amazon.CDK.AWS.CloudFront;
using Amazon.CDK.AWS.CloudFront.Origins;
using Amazon.CDK.AWS.Route53;
using Amazon.CDK.AWS.Route53.Targets;
using Amazon.CDK.AWS.S3;
using Amazon.CDK.AWS.S3.Deployment;
using Constructs;
using LayeredCraft.Cdk.Constructs.Models;

namespace LayeredCraft.Cdk.Constructs;

/// <summary>
/// AWS CDK construct for hosting immutable, versioned web assets in a private S3 bucket and serving them
/// through a CloudFront distribution that reaches the bucket with an origin access control (OAC). No website
/// endpoint and no public access are used; optional custom DNS, SPA routing and error rewrites are off by
/// default.
/// </summary>
public class PrivateStaticAssetsConstruct : Construct
{
    /// <summary>Gets the private S3 bucket that stores the assets.</summary>
    public IBucket Bucket { get; }

    /// <summary>Gets the CloudFront distribution that serves the assets.</summary>
    public IDistribution Distribution { get; }

    /// <summary>
    /// Gets the primary domain name: the first configured custom domain name when DNS is configured, otherwise
    /// the distribution's CloudFront domain name.
    /// </summary>
    public string DomainName { get; }

    /// <summary>Initializes a new instance of the <see cref="PrivateStaticAssetsConstruct"/> class.</summary>
    /// <param name="scope">The parent construct.</param>
    /// <param name="id">The construct ID.</param>
    /// <param name="props">Configuration properties for the construct.</param>
    public PrivateStaticAssetsConstruct(Construct scope, string id, IPrivateStaticAssetsConstructProps props)
        : base(scope, id)
    {
        ArgumentNullException.ThrowIfNull(props);

        var bucket = new Bucket(this, $"{id}-bucket", new BucketProps
        {
            BlockPublicAccess = BlockPublicAccess.BLOCK_ALL,
            Encryption = BucketEncryption.S3_MANAGED,
            EnforceSSL = true,
            Versioned = true,
            RemovalPolicy = props.RemovalPolicy,
        });

        Bucket = bucket;

        ResolveDomain(props.Domain, id, out var domainNames, out var certificate, out var zone);

        Amazon.CDK.AWS.CloudFront.Function? spaRewriteFunction = null;
        if (props.SpaRouting)
        {
            spaRewriteFunction = new Amazon.CDK.AWS.CloudFront.Function(this, $"{id}-spa-rewrite",
                new FunctionProps
                {
                    Code = FunctionCode.FromInline("""
                        function handler(event) {
                            var uri = event.request.uri;
                            if (!uri.match(/\.[a-zA-Z0-9]+$/)) {
                                event.request.uri = '/index.html';
                            }
                            return event.request;
                        }
                        """),
                    Runtime = FunctionRuntime.JS_2_0,
                    Comment = "Rewrite SPA routes to /index.html for client-side routing",
                });
        }

        FunctionAssociation[]? functionAssociations = spaRewriteFunction is null
            ? null
            :
            [
                new FunctionAssociation
                {
                    Function = spaRewriteFunction,
                    EventType = FunctionEventType.VIEWER_REQUEST,
                },
            ];

        var distribution = new Distribution(this, $"{id}-cdn", new DistributionProps
        {
            DefaultBehavior = new BehaviorOptions
            {
                Origin = S3BucketOrigin.WithOriginAccessControl(bucket),
                AllowedMethods = AllowedMethods.ALLOW_GET_HEAD,
                Compress = true,
                ViewerProtocolPolicy = props.ViewerProtocolPolicy,
                CachePolicy = props.CachePolicy,
                FunctionAssociations = functionAssociations,
            },
            Comment = props.Comment,
            ErrorResponses = props.ErrorResponses.Length > 0 ? props.ErrorResponses : null,
            DefaultRootObject = props.DefaultRootObject,
            DomainNames = domainNames,
            Certificate = certificate,
        });

        Distribution = distribution;
        DomainName = domainNames is not null ? domainNames[0] : distribution.DomainName;

        if (zone is not null && domainNames is not null)
        {
            for (var index = 0; index < domainNames.Length; index++)
            {
                _ = new ARecord(this, $"{id}-a-record-{index}", new ARecordProps
                {
                    Zone = zone,
                    RecordName = domainNames[index],
                    Target = RecordTarget.FromAlias(new CloudFrontTarget(distribution)),
                });

                _ = new AaaaRecord(this, $"{id}-aaaa-record-{index}", new AaaaRecordProps
                {
                    Zone = zone,
                    RecordName = domainNames[index],
                    Target = RecordTarget.FromAlias(new CloudFrontTarget(distribution)),
                });
            }
        }

        var deploymentProps = new BucketDeploymentProps
        {
            Sources = [Source.Asset(props.AssetPath)],
            DestinationBucket = bucket,
            DestinationKeyPrefix = props.DestinationKeyPrefix,
            Prune = props.Prune,
            RetainOnDelete = props.RetainOnDelete,
            CacheControl = props.CacheControl.Length > 0 ? props.CacheControl : null,
        };

        if (props.InvalidationPaths is { Length: > 0 } invalidationPaths)
        {
            deploymentProps.Distribution = distribution;
            deploymentProps.DistributionPaths = invalidationPaths;
        }

        _ = new BucketDeployment(this, $"{id}-deployment", deploymentProps);
    }

    private void ResolveDomain(
        PrivateStaticAssetsDomainOptions? domain,
        string id,
        out string[]? domainNames,
        out ICertificate? certificate,
        out IHostedZone? zone)
    {
        domainNames = null;
        certificate = null;
        zone = null;

        if (domain is null)
        {
            return;
        }

        if (domain.DomainNames.Length == 0)
        {
            throw new ArgumentException(
                "At least one domain name is required when a custom domain is configured.", nameof(domain));
        }

        if (domain.HostedZone is not null && domain.HostedZoneName is not null)
        {
            throw new ArgumentException(
                "Specify either HostedZone or HostedZoneName, not both.", nameof(domain));
        }

        zone = domain.HostedZone
            ?? (domain.HostedZoneName is not null
                ? HostedZone.FromLookup(this, $"{id}-zone", new HostedZoneProviderProps
                {
                    DomainName = domain.HostedZoneName,
                })
                : null);

        if (domain.Certificate is null && zone is null)
        {
            throw new ArgumentException(
                "A custom domain requires a Certificate, or a HostedZone (or HostedZoneName) to create one.",
                nameof(domain));
        }

        certificate = domain.Certificate
            ?? new Certificate(this, $"{id}-certificate", new CertificateProps
            {
                DomainName = domain.DomainNames[0],
                SubjectAlternativeNames = domain.DomainNames.Length > 1 ? domain.DomainNames[1..] : null,
                Validation = CertificateValidation.FromDns(zone),
            });

        domainNames = domain.DomainNames;
    }
}

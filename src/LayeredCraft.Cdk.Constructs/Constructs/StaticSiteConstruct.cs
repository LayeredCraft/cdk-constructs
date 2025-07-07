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

namespace LayeredCraft.Cdk.Constructs.Constructs;

/// <summary>
/// AWS CDK construct for creating a complete static website infrastructure.
/// This construct creates an S3 bucket for hosting static content, CloudFront distribution for global content delivery,
/// SSL certificate for HTTPS, and Route53 DNS records for domain configuration.
/// Optionally supports API proxying through CloudFront behaviors.
/// </summary>
public class StaticSiteConstruct : Construct
{
    /// <summary>
    /// Initializes a new instance of the StaticSiteConstruct class.
    /// </summary>
    /// <param name="scope">The parent construct</param>
    /// <param name="id">The construct ID</param>
    /// <param name="props">Configuration properties for the static site</param>
    public StaticSiteConstruct(Construct scope, string id, IStaticSiteConstructProps props) : base(scope, id)
    {
        // Lookup existing Route53 hosted zone for the domain
        var zone = HostedZone.FromLookup(this, id, new HostedZoneProviderProps
        {
            DomainName = props.DomainName
        });

        var siteDomain = $"{props.SiteSubDomain}.{props.DomainName}";
        
        // Create S3 bucket for static website hosting
        var siteBucket = new Bucket(this, $"{id}-bucket", new BucketProps
        {
            BucketName = siteDomain,
            WebsiteIndexDocument = "index.html",
            WebsiteErrorDocument = "index.html",
            PublicReadAccess = true,
            BlockPublicAccess = new BlockPublicAccess(new BlockPublicAccessOptions
            {
                BlockPublicPolicy = false,
                BlockPublicAcls = false,
                IgnorePublicAcls = false,
                RestrictPublicBuckets = false
            }),
            RemovalPolicy = RemovalPolicy.DESTROY,
            Versioned = false,
            AutoDeleteObjects = true,
            LifecycleRules = [new LifecycleRule
            {
                Enabled = true,
                NoncurrentVersionExpiration = Duration.Days(1)
            }]
        });

        // Create SSL certificate for HTTPS with DNS validation
        var certificate = new Certificate(this, $"{id}-certificate", new CertificateProps
        {
            DomainName = siteDomain,
            Validation = CertificateValidation.FromDns(zone),
            SubjectAlternativeNames = props.AlternateDomains,
        });

        // Create CloudFront distribution for global content delivery
        var distribution = new Distribution(this, $"{id}-cdn", new DistributionProps
        {
            DomainNames = ((string[]) [siteDomain]).Concat(props.AlternateDomains).ToArray(),
            DefaultBehavior = new BehaviorOptions
            {
                Origin = new S3StaticWebsiteOrigin(siteBucket, new S3StaticWebsiteOriginProps
                {
                    ProtocolPolicy = OriginProtocolPolicy.HTTP_ONLY
                }),
                AllowedMethods = AllowedMethods.ALLOW_GET_HEAD,
                Compress = true
            },
            Certificate = certificate,
            ErrorResponses =
            [
                new ErrorResponse
                {
                    HttpStatus = 403,
                    ResponseHttpStatus = 200,
                    ResponsePagePath = "/index.html"
                }
            ]
        });

        // Add API proxying behavior if API domain is specified
        if (!string.IsNullOrWhiteSpace(props.ApiDomain))
        {
            distribution.AddBehavior("/api/*", new HttpOrigin(props.ApiDomain, new HttpOriginProps
            {
                ProtocolPolicy = OriginProtocolPolicy.HTTPS_ONLY
            }), new BehaviorOptions
            {
                AllowedMethods = AllowedMethods.ALLOW_ALL,
                CachePolicy = CachePolicy.CACHING_DISABLED,
                OriginRequestPolicy = OriginRequestPolicy.ALL_VIEWER_EXCEPT_HOST_HEADER,
                ViewerProtocolPolicy = ViewerProtocolPolicy.REDIRECT_TO_HTTPS,
                Compress = true
            });
        }
        
        // Create primary DNS A record for the site domain
        _ = new ARecord(this, $"{id}-alias-record", new ARecordProps
        {
            Zone = zone,
            RecordName = siteDomain,
            Target = RecordTarget.FromAlias(new CloudFrontTarget(distribution)),
        });

        // Create DNS A records for alternate domains
        var counter = 0;
        foreach (var domain in props.AlternateDomains)
        {
            _ = new ARecord(this, $"{id}-alias-record-{counter++}", new ARecordProps
            {
                Zone = zone,
                RecordName = domain,
                Target = RecordTarget.FromAlias(new CloudFrontTarget(distribution))
            });
        }

        // Deploy static assets to S3 bucket and invalidate CloudFront cache
        _ = new BucketDeployment(this, $"{id}-deployment", new BucketDeploymentProps
        {
            Sources = [Source.Asset(props.AssetPath)],
            DestinationBucket = siteBucket,
            Distribution = distribution,
            DistributionPaths = ["/*"],
            MemoryLimit = 1024
        });
    }
}
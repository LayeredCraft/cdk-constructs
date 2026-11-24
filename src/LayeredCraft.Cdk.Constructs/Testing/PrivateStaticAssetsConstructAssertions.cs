using Amazon.CDK.Assertions;

namespace LayeredCraft.Cdk.Constructs.Testing;

/// <summary>
/// Extension methods for asserting <see cref="PrivateStaticAssetsConstruct"/> resources in CDK templates. These
/// helpers simplify the common security and deployment scenarios for consumers of the library.
/// </summary>
public static class PrivateStaticAssetsConstructAssertions
{
    /// <summary>
    /// Asserts that the template contains a private, versioned, SSE-S3 bucket with Block Public Access fully on.
    /// </summary>
    /// <param name="template">The CDK template to assert against.</param>
    public static void ShouldHavePrivateBucket(this Template template)
    {
        template.HasResourceProperties("AWS::S3::Bucket", Match.ObjectLike(new Dictionary<string, object>
        {
            ["BucketEncryption"] = Match.ObjectLike(new Dictionary<string, object>
            {
                ["ServerSideEncryptionConfiguration"] = Match.ArrayWith(new object[]
                {
                    Match.ObjectLike(new Dictionary<string, object>
                    {
                        ["ServerSideEncryptionByDefault"] = Match.ObjectLike(new Dictionary<string, object>
                        {
                            ["SSEAlgorithm"] = "AES256",
                        }),
                    }),
                }),
            }),
            ["PublicAccessBlockConfiguration"] = Match.ObjectLike(new Dictionary<string, object>
            {
                ["BlockPublicAcls"] = true,
                ["BlockPublicPolicy"] = true,
                ["IgnorePublicAcls"] = true,
                ["RestrictPublicBuckets"] = true,
            }),
            ["VersioningConfiguration"] = Match.ObjectLike(new Dictionary<string, object>
            {
                ["Status"] = "Enabled",
            }),
        }));
    }

    /// <summary>Asserts that the template's bucket has no static website configuration.</summary>
    /// <param name="template">The CDK template to assert against.</param>
    public static void ShouldNotHaveWebsiteEndpoint(this Template template)
    {
        template.HasResourceProperties("AWS::S3::Bucket", Match.Not(Match.ObjectLike(new Dictionary<string, object>
        {
            ["WebsiteConfiguration"] = Match.AnyValue(),
        })));
    }

    /// <summary>
    /// Asserts that the distribution reaches the bucket through an origin access control and that an
    /// <c>AWS::CloudFront::OriginAccessControl</c> of origin type <c>s3</c> exists.
    /// </summary>
    /// <param name="template">The CDK template to assert against.</param>
    public static void ShouldHaveOacOnlyOrigin(this Template template)
    {
        template.HasResourceProperties("AWS::CloudFront::OriginAccessControl", Match.ObjectLike(new Dictionary<string, object>
        {
            ["OriginAccessControlConfig"] = Match.ObjectLike(new Dictionary<string, object>
            {
                ["OriginAccessControlOriginType"] = "s3",
            }),
        }));

        template.HasResourceProperties("AWS::CloudFront::Distribution", Match.ObjectLike(new Dictionary<string, object>
        {
            ["DistributionConfig"] = Match.ObjectLike(new Dictionary<string, object>
            {
                ["Origins"] = Match.ArrayWith(new object[]
                {
                    Match.ObjectLike(new Dictionary<string, object>
                    {
                        ["OriginAccessControlId"] = Match.AnyValue(),
                    }),
                }),
            }),
        }));
    }

    /// <summary>
    /// Asserts that the bucket policy grants the CloudFront service <c>s3:GetObject</c>, conditioned on the
    /// distribution's <c>AWS:SourceArn</c>.
    /// </summary>
    /// <param name="template">The CDK template to assert against.</param>
    public static void ShouldScopeBucketPolicyToDistribution(this Template template)
    {
        template.HasResourceProperties("AWS::S3::BucketPolicy", Match.ObjectLike(new Dictionary<string, object>
        {
            ["PolicyDocument"] = Match.ObjectLike(new Dictionary<string, object>
            {
                ["Statement"] = Match.ArrayWith(new object[]
                {
                    Match.ObjectLike(new Dictionary<string, object>
                    {
                        ["Effect"] = "Allow",
                        ["Action"] = "s3:GetObject",
                        ["Principal"] = Match.ObjectLike(new Dictionary<string, object>
                        {
                            ["Service"] = "cloudfront.amazonaws.com",
                        }),
                        ["Condition"] = Match.ObjectLike(new Dictionary<string, object>
                        {
                            ["StringEquals"] = Match.ObjectLike(new Dictionary<string, object>
                            {
                                ["AWS:SourceArn"] = Match.AnyValue(),
                            }),
                        }),
                    }),
                }),
            }),
        }));
    }

    /// <summary>Asserts that the distribution's default behavior is HTTPS-only.</summary>
    /// <param name="template">The CDK template to assert against.</param>
    public static void ShouldHaveHttpsOnlyViewerPolicy(this Template template)
    {
        template.HasResourceProperties("AWS::CloudFront::Distribution", Match.ObjectLike(new Dictionary<string, object>
        {
            ["DistributionConfig"] = Match.ObjectLike(new Dictionary<string, object>
            {
                ["DefaultCacheBehavior"] = Match.ObjectLike(new Dictionary<string, object>
                {
                    ["ViewerProtocolPolicy"] = "https-only",
                }),
            }),
        }));
    }

    /// <summary>
    /// Asserts the bucket deployment's immutable settings: the destination key prefix (when supplied), the
    /// prune and retain-on-delete flags, and the joined <c>Cache-Control</c> value (when supplied).
    /// </summary>
    /// <param name="template">The CDK template to assert against.</param>
    /// <param name="destinationKeyPrefix">Expected destination key prefix, or <c>null</c> to skip the check.</param>
    /// <param name="prune">Expected prune flag (default <c>false</c>).</param>
    /// <param name="retainOnDelete">Expected retain-on-delete flag (default <c>true</c>).</param>
    /// <param name="cacheControl">Expected joined cache-control value, or <c>null</c> to skip the check.</param>
    public static void ShouldHaveBucketDeployment(this Template template, string? destinationKeyPrefix = null,
        bool prune = false, bool retainOnDelete = true, string? cacheControl = null)
    {
        var properties = new Dictionary<string, object>
        {
            ["DestinationBucketName"] = Match.AnyValue(),
            ["Prune"] = prune,
            ["RetainOnDelete"] = retainOnDelete,
        };

        if (destinationKeyPrefix is not null)
        {
            properties["DestinationBucketKeyPrefix"] = destinationKeyPrefix;
        }

        if (cacheControl is not null)
        {
            properties["SystemMetadata"] = Match.ObjectLike(new Dictionary<string, object>
            {
                ["cache-control"] = cacheControl,
            });
        }

        template.HasResourceProperties("Custom::CDKBucketDeployment", Match.ObjectLike(properties));
    }

    /// <summary>
    /// Asserts that an A and an AAAA alias record exist for every supplied domain name.
    /// </summary>
    /// <param name="template">The CDK template to assert against.</param>
    /// <param name="domainNames">The domain names that should have alias records.</param>
    public static void ShouldHaveDnsRecords(this Template template, params string[] domainNames)
    {
        foreach (var domainName in domainNames)
        {
            template.HasResourceProperties("AWS::Route53::RecordSet", Match.ObjectLike(new Dictionary<string, object>
            {
                ["Name"] = $"{domainName}.",
                ["Type"] = "A",
                ["AliasTarget"] = Match.ObjectLike(new Dictionary<string, object>
                {
                    ["DNSName"] = Match.AnyValue(),
                    ["HostedZoneId"] = Match.AnyValue(),
                }),
            }));

            template.HasResourceProperties("AWS::Route53::RecordSet", Match.ObjectLike(new Dictionary<string, object>
            {
                ["Name"] = $"{domainName}.",
                ["Type"] = "AAAA",
                ["AliasTarget"] = Match.ObjectLike(new Dictionary<string, object>
                {
                    ["DNSName"] = Match.AnyValue(),
                    ["HostedZoneId"] = Match.AnyValue(),
                }),
            }));
        }
    }
}

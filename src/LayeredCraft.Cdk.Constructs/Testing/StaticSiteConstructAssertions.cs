using Amazon.CDK.Assertions;

namespace LayeredCraft.Cdk.Constructs.Testing;

/// <summary>
/// Extension methods for asserting StaticSiteConstruct resources in CDK templates.
/// These helpers simplify common testing scenarios for consumers of the library.
/// </summary>
public static class StaticSiteConstructAssertions
{
    /// <summary>
    /// Asserts that the template contains an S3 bucket with static website hosting configuration.
    /// </summary>
    /// <param name="template">The CDK template to assert against</param>
    /// <param name="bucketName">The expected bucket name</param>
    public static void ShouldHaveStaticWebsiteBucket(this Template template, string bucketName)
    {
        template.HasResourceProperties("AWS::S3::Bucket", Match.ObjectLike(new Dictionary<string, object>
        {
            { "BucketName", bucketName },
            { "WebsiteConfiguration", Match.ObjectLike(new Dictionary<string, object>
            {
                { "IndexDocument", "index.html" },
                { "ErrorDocument", "index.html" }
            }) },
            { "PublicAccessBlockConfiguration", Match.ObjectLike(new Dictionary<string, object>
            {
                { "BlockPublicPolicy", false },
                { "BlockPublicAcls", false },
                { "IgnorePublicAcls", false },
                { "RestrictPublicBuckets", false }
            }) }
        }));
    }

    /// <summary>
    /// Asserts that the template contains a CloudFront distribution with the specified domain.
    /// </summary>
    /// <param name="template">The CDK template to assert against</param>
    /// <param name="domainName">The expected primary domain name</param>
    public static void ShouldHaveCloudFrontDistribution(this Template template, string domainName)
    {
        template.HasResourceProperties("AWS::CloudFront::Distribution", Match.ObjectLike(new Dictionary<string, object>
        {
            { "DistributionConfig", Match.ObjectLike(new Dictionary<string, object>
            {
                { "Aliases", Match.ArrayWith([domainName]) },
                { "Enabled", true },
                { "DefaultRootObject", Match.Absent() },
                { "CustomErrorResponses", Match.ArrayWith([
                    Match.ObjectLike(new Dictionary<string, object>
                    {
                        { "ErrorCode", 403 },
                        { "ResponseCode", 200 },
                        { "ResponsePagePath", "/index.html" }
                    })
                ]) }
            }) }
        }));
    }

    /// <summary>
    /// Asserts that the template contains a CloudFront distribution with API behavior for /api/* paths.
    /// </summary>
    /// <param name="template">The CDK template to assert against</param>
    /// <param name="apiDomain">The expected API domain</param>
    public static void ShouldHaveApiProxyBehavior(this Template template, string apiDomain)
    {
        template.HasResourceProperties("AWS::CloudFront::Distribution", Match.ObjectLike(new Dictionary<string, object>
        {
            { "DistributionConfig", Match.ObjectLike(new Dictionary<string, object>
            {
                { "CacheBehaviors", Match.ArrayWith([
                    Match.ObjectLike(new Dictionary<string, object>
                    {
                        { "PathPattern", "/api/*" },
                        { "TargetOriginId", Match.AnyValue() },
                        { "ViewerProtocolPolicy", "redirect-to-https" },
                        { "AllowedMethods", Match.AnyValue() },
                        { "CachePolicyId", Match.AnyValue() },
                        { "OriginRequestPolicyId", Match.AnyValue() },
                        { "Compress", true }
                    })
                ]) }
            }) }
        }));

        // Also verify the HTTP origin exists for the API domain
        template.HasResourceProperties("AWS::CloudFront::Distribution", Match.ObjectLike(new Dictionary<string, object>
        {
            { "DistributionConfig", Match.ObjectLike(new Dictionary<string, object>
            {
                { "Origins", Match.ArrayWith([
                    Match.ObjectLike(new Dictionary<string, object>
                    {
                        { "DomainName", apiDomain },
                        { "CustomOriginConfig", Match.ObjectLike(new Dictionary<string, object>
                        {
                            { "OriginProtocolPolicy", "https-only" }
                        }) }
                    })
                ]) }
            }) }
        }));
    }

    /// <summary>
    /// Asserts that the template does not contain API proxy behavior (no /api/* cache behaviors).
    /// </summary>
    /// <param name="template">The CDK template to assert against</param>
    public static void ShouldNotHaveApiProxyBehavior(this Template template)
    {
        // Check that there are no cache behaviors for /api/*
        template.HasResourceProperties("AWS::CloudFront::Distribution", Match.Not(Match.ObjectLike(new Dictionary<string, object>
        {
            { "DistributionConfig", Match.ObjectLike(new Dictionary<string, object>
            {
                { "CacheBehaviors", Match.ArrayWith([
                    Match.ObjectLike(new Dictionary<string, object>
                    {
                        { "PathPattern", "/api/*" }
                    })
                ]) }
            }) }
        })));
    }

    /// <summary>
    /// Asserts that the template contains an SSL certificate for the specified domain.
    /// </summary>
    /// <param name="template">The CDK template to assert against</param>
    /// <param name="domainName">The expected primary domain name</param>
    public static void ShouldHaveSSLCertificate(this Template template, string domainName)
    {
        template.HasResourceProperties("AWS::CertificateManager::Certificate", Match.ObjectLike(new Dictionary<string, object>
        {
            { "DomainName", domainName },
            { "ValidationMethod", "DNS" }
        }));
    }

    /// <summary>
    /// Asserts that the template contains Route53 A records for the specified domain.
    /// </summary>
    /// <param name="template">The CDK template to assert against</param>
    /// <param name="domainName">The expected domain name</param>
    public static void ShouldHaveRoute53Records(this Template template, string domainName)
    {
        template.HasResourceProperties("AWS::Route53::RecordSet", Match.ObjectLike(new Dictionary<string, object>
        {
            { "Name", $"{domainName}." },
            { "Type", "A" },
            { "AliasTarget", Match.ObjectLike(new Dictionary<string, object>
            {
                { "DNSName", Match.AnyValue() },
                { "HostedZoneId", Match.AnyValue() }
            }) }
        }));
    }

    /// <summary>
    /// Asserts that the template contains multiple Route53 A records for alternate domains.
    /// </summary>
    /// <param name="template">The CDK template to assert against</param>
    /// <param name="domainCount">Expected number of domain records (primary + alternates)</param>
    public static void ShouldHaveMultipleRoute53Records(this Template template, int domainCount)
    {
        template.ResourceCountIs("AWS::Route53::RecordSet", domainCount);
    }

    /// <summary>
    /// Asserts that the template contains a bucket deployment for static assets.
    /// CDK BucketDeployment creates a Custom::CDKBucketDeployment resource, not AWS::S3::BucketDeployment.
    /// </summary>
    /// <param name="template">The CDK template to assert against</param>
    public static void ShouldHaveBucketDeployment(this Template template)
    {
        template.HasResourceProperties("Custom::CDKBucketDeployment", Match.ObjectLike(new Dictionary<string, object>
        {
            { "DestinationBucketName", Match.AnyValue() },
            { "SourceBucketNames", Match.AnyValue() },
            { "DistributionId", Match.AnyValue() },
            { "DistributionPaths", Match.ArrayWith(["/*"]) }
        }));
    }

    /// <summary>
    /// Asserts that the template contains all expected resources for a complete static site.
    /// </summary>
    /// <param name="template">The CDK template to assert against</param>
    /// <param name="bucketName">The expected S3 bucket name</param>
    /// <param name="domainName">The expected primary domain name</param>
    /// <param name="hasApiProxy">Whether API proxy behavior should be present</param>
    /// <param name="domainCount">Expected number of domain records</param>
    public static void ShouldHaveCompleteStaticSite(this Template template, string bucketName, string domainName, 
        bool hasApiProxy = false, int domainCount = 1)
    {
        template.ShouldHaveStaticWebsiteBucket(bucketName);
        template.ShouldHaveCloudFrontDistribution(domainName);
        template.ShouldHaveSSLCertificate(domainName);
        template.ShouldHaveRoute53Records(domainName);
        template.ShouldHaveMultipleRoute53Records(domainCount);
        template.ShouldHaveBucketDeployment();

        if (hasApiProxy)
        {
            // Note: API domain assertion would need the actual API domain value
            // This is a simplified check for cache behavior existence
            template.HasResourceProperties("AWS::CloudFront::Distribution", Match.ObjectLike(new Dictionary<string, object>
            {
                { "DistributionConfig", Match.ObjectLike(new Dictionary<string, object>
                {
                    { "CacheBehaviors", Match.AnyValue() }
                }) }
            }));
        }
        else
        {
            template.ShouldNotHaveApiProxyBehavior();
        }
    }
}
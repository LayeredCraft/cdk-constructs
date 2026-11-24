using Amazon.CDK;
using Amazon.CDK.AWS.CertificateManager;
using Amazon.CDK.AWS.CloudFront;
using Amazon.CDK.AWS.Route53;
using Amazon.CDK.AWS.S3.Deployment;
using Amazon.CDK.Assertions;
using AwesomeAssertions;
using Compono.XunitV3;
using LayeredCraft.Cdk.Constructs;
using LayeredCraft.Cdk.Constructs.Models;
using LayeredCraft.Cdk.Constructs.Testing;
using LayeredCraft.Cdk.Constructs.Tests.TestKit.Profiles;

namespace LayeredCraft.Cdk.Constructs.Tests;

/// <summary>
/// Behavior tests for <see cref="PrivateStaticAssetsConstruct"/>: the private bucket contract (Block Public
/// Access, SSE-S3, SSL-only, versioning, no website endpoint), the OAC-only CloudFront origin, the immutable
/// deployment, optional SPA routing, error responses and custom DNS, and the public properties.
/// </summary>
[Collection("CDK Tests")]
public class PrivateStaticAssetsConstructTests
{
    private const string AssetPath = "TestAssets/static-site";

    private static PrivateStaticAssetsConstructProps CreateProps() => new()
    {
        AssetPath = CdkTestHelper.GetTestAssetPath(AssetPath),
    };

    // ---- Bucket: Block Public Access, SSE-S3, versioning, no website endpoint ----

    [Fact]
    public void Construct_ShouldBlockAllPublicAccessOnBucket()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();

        _ = new PrivateStaticAssetsConstruct(stack, "assets", CreateProps());

        var template = Template.FromStack(stack);
        template.HasResourceProperties("AWS::S3::Bucket", Match.ObjectLike(new Dictionary<string, object>
        {
            ["PublicAccessBlockConfiguration"] = Match.ObjectLike(new Dictionary<string, object>
            {
                ["BlockPublicAcls"] = true,
                ["BlockPublicPolicy"] = true,
                ["IgnorePublicAcls"] = true,
                ["RestrictPublicBuckets"] = true,
            }),
        }));
    }

    [Fact]
    public void Construct_ShouldEncryptBucketWithSseS3()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();

        _ = new PrivateStaticAssetsConstruct(stack, "assets", CreateProps());

        var template = Template.FromStack(stack);
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
        }));
    }

    [Fact]
    public void Construct_ShouldVersionBucket()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();

        _ = new PrivateStaticAssetsConstruct(stack, "assets", CreateProps());

        var template = Template.FromStack(stack);
        template.HasResourceProperties("AWS::S3::Bucket", Match.ObjectLike(new Dictionary<string, object>
        {
            ["VersioningConfiguration"] = Match.ObjectLike(new Dictionary<string, object>
            {
                ["Status"] = "Enabled",
            }),
        }));
    }

    [Fact]
    public void Construct_ShouldNotConfigureWebsiteEndpoint()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();

        _ = new PrivateStaticAssetsConstruct(stack, "assets", CreateProps());

        var template = Template.FromStack(stack);
        template.HasResourceProperties("AWS::S3::Bucket", Match.Not(Match.ObjectLike(new Dictionary<string, object>
        {
            ["WebsiteConfiguration"] = Match.AnyValue(),
        })));
    }

    [Fact]
    public void Construct_ShouldEnforceSslOnlyBucketPolicy()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();

        _ = new PrivateStaticAssetsConstruct(stack, "assets", CreateProps());

        var template = Template.FromStack(stack);
        template.HasResourceProperties("AWS::S3::BucketPolicy", Match.ObjectLike(new Dictionary<string, object>
        {
            ["PolicyDocument"] = Match.ObjectLike(new Dictionary<string, object>
            {
                ["Statement"] = Match.ArrayWith(new object[]
                {
                    Match.ObjectLike(new Dictionary<string, object>
                    {
                        ["Effect"] = "Deny",
                        ["Action"] = "s3:*",
                        ["Condition"] = Match.ObjectLike(new Dictionary<string, object>
                        {
                            ["Bool"] = Match.ObjectLike(new Dictionary<string, object>
                            {
                                ["aws:SecureTransport"] = "false",
                            }),
                        }),
                    }),
                }),
            }),
        }));
    }

    // ---- Bucket removal policy ----

    [Fact]
    public void Construct_ShouldRetainBucketByDefault()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();

        _ = new PrivateStaticAssetsConstruct(stack, "assets", CreateProps());

        var template = Template.FromStack(stack);
        template.HasResource("AWS::S3::Bucket", Match.ObjectLike(new Dictionary<string, object>
        {
            ["DeletionPolicy"] = "Retain",
        }));
    }

    [Fact]
    public void Construct_ShouldHonorConfiguredRemovalPolicy()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        var props = CreateProps();
        props.RemovalPolicy = RemovalPolicy.DESTROY;

        _ = new PrivateStaticAssetsConstruct(stack, "assets", props);

        var template = Template.FromStack(stack);
        template.HasResource("AWS::S3::Bucket", Match.ObjectLike(new Dictionary<string, object>
        {
            ["DeletionPolicy"] = "Delete",
        }));
    }

    // ---- CloudFront: OAC-only origin, scoped bucket policy, viewer and cache policy ----

    [Fact]
    public void Construct_ShouldCreateDistributionWithOriginAccessControl()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();

        _ = new PrivateStaticAssetsConstruct(stack, "assets", CreateProps());

        var template = Template.FromStack(stack);
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

    [Fact]
    public void Construct_ShouldScopeBucketPolicyToDistribution()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();

        _ = new PrivateStaticAssetsConstruct(stack, "assets", CreateProps());

        var template = Template.FromStack(stack);
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

    [Fact]
    public void Construct_ShouldNotGrantPublicBucketAccess()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();

        _ = new PrivateStaticAssetsConstruct(stack, "assets", CreateProps());

        var template = Template.FromStack(stack);
        template.HasResourceProperties("AWS::S3::BucketPolicy", Match.Not(Match.ObjectLike(new Dictionary<string, object>
        {
            ["PolicyDocument"] = Match.ObjectLike(new Dictionary<string, object>
            {
                ["Statement"] = Match.ArrayWith(new object[]
                {
                    Match.ObjectLike(new Dictionary<string, object>
                    {
                        ["Principal"] = "*",
                    }),
                }),
            }),
        })));
    }

    [Fact]
    public void Construct_ShouldUseHttpsOnlyViewerPolicyByDefault()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();

        _ = new PrivateStaticAssetsConstruct(stack, "assets", CreateProps());

        var template = Template.FromStack(stack);
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

    [Fact]
    public void Construct_ShouldHonorRedirectViewerPolicy()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        var props = CreateProps();
        props.ViewerProtocolPolicy = ViewerProtocolPolicy.REDIRECT_TO_HTTPS;

        _ = new PrivateStaticAssetsConstruct(stack, "assets", props);

        var template = Template.FromStack(stack);
        template.HasResourceProperties("AWS::CloudFront::Distribution", Match.ObjectLike(new Dictionary<string, object>
        {
            ["DistributionConfig"] = Match.ObjectLike(new Dictionary<string, object>
            {
                ["DefaultCacheBehavior"] = Match.ObjectLike(new Dictionary<string, object>
                {
                    ["ViewerProtocolPolicy"] = "redirect-to-https",
                }),
            }),
        }));
    }

    [Fact]
    public void Construct_ShouldUseCachingOptimizedPolicyByDefault()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();

        _ = new PrivateStaticAssetsConstruct(stack, "assets", CreateProps());

        var template = Template.FromStack(stack);
        template.HasResourceProperties("AWS::CloudFront::Distribution", Match.ObjectLike(new Dictionary<string, object>
        {
            ["DistributionConfig"] = Match.ObjectLike(new Dictionary<string, object>
            {
                ["DefaultCacheBehavior"] = Match.ObjectLike(new Dictionary<string, object>
                {
                    ["CachePolicyId"] = "658327ea-f89d-4fab-a63d-7e88639e58f6",
                }),
            }),
        }));
    }

    [Fact]
    public void Construct_ShouldHonorCustomCachePolicy()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        var props = CreateProps();
        props.CachePolicy = CachePolicy.CACHING_DISABLED;

        _ = new PrivateStaticAssetsConstruct(stack, "assets", props);

        var template = Template.FromStack(stack);
        template.HasResourceProperties("AWS::CloudFront::Distribution", Match.ObjectLike(new Dictionary<string, object>
        {
            ["DistributionConfig"] = Match.ObjectLike(new Dictionary<string, object>
            {
                ["DefaultCacheBehavior"] = Match.ObjectLike(new Dictionary<string, object>
                {
                    ["CachePolicyId"] = "4135ea2d-6df8-44a3-9df3-4b5a84be39ad",
                }),
            }),
        }));
    }

    // ---- SPA routing and error rewrites (off by default) ----

    [Fact]
    public void Construct_ShouldNotAddSpaFunctionByDefault()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();

        _ = new PrivateStaticAssetsConstruct(stack, "assets", CreateProps());

        var template = Template.FromStack(stack);
        template.ResourceCountIs("AWS::CloudFront::Function", 0);
        template.HasResourceProperties("AWS::CloudFront::Distribution", Match.Not(Match.ObjectLike(new Dictionary<string, object>
        {
            ["DistributionConfig"] = Match.ObjectLike(new Dictionary<string, object>
            {
                ["DefaultCacheBehavior"] = Match.ObjectLike(new Dictionary<string, object>
                {
                    ["FunctionAssociations"] = Match.AnyValue(),
                }),
            }),
        })));
    }

    [Fact]
    public void Construct_ShouldNotAddErrorResponsesByDefault()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();

        _ = new PrivateStaticAssetsConstruct(stack, "assets", CreateProps());

        var template = Template.FromStack(stack);
        template.HasResourceProperties("AWS::CloudFront::Distribution", Match.Not(Match.ObjectLike(new Dictionary<string, object>
        {
            ["DistributionConfig"] = Match.ObjectLike(new Dictionary<string, object>
            {
                ["CustomErrorResponses"] = Match.AnyValue(),
            }),
        })));
    }

    [Fact]
    public void Construct_ShouldNotSetDefaultRootObjectByDefault()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();

        _ = new PrivateStaticAssetsConstruct(stack, "assets", CreateProps());

        var template = Template.FromStack(stack);
        template.HasResourceProperties("AWS::CloudFront::Distribution", Match.Not(Match.ObjectLike(new Dictionary<string, object>
        {
            ["DistributionConfig"] = Match.ObjectLike(new Dictionary<string, object>
            {
                ["DefaultRootObject"] = Match.AnyValue(),
            }),
        })));
    }

    [Fact]
    public void Construct_ShouldAddSpaRewriteFunctionWhenEnabled()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        var props = CreateProps();
        props.SpaRouting = true;

        _ = new PrivateStaticAssetsConstruct(stack, "assets", props);

        var template = Template.FromStack(stack);
        template.HasResourceProperties("AWS::CloudFront::Function", Match.ObjectLike(new Dictionary<string, object>
        {
            ["FunctionConfig"] = Match.ObjectLike(new Dictionary<string, object>
            {
                ["Runtime"] = "cloudfront-js-2.0",
            }),
        }));
        template.HasResourceProperties("AWS::CloudFront::Distribution", Match.ObjectLike(new Dictionary<string, object>
        {
            ["DistributionConfig"] = Match.ObjectLike(new Dictionary<string, object>
            {
                ["DefaultCacheBehavior"] = Match.ObjectLike(new Dictionary<string, object>
                {
                    ["FunctionAssociations"] = Match.ArrayWith(new object[]
                    {
                        Match.ObjectLike(new Dictionary<string, object>
                        {
                            ["EventType"] = "viewer-request",
                        }),
                    }),
                }),
            }),
        }));
    }

    [Fact]
    public void Construct_ShouldSetErrorResponses()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        var props = CreateProps();
        props.ErrorResponses =
        [
            new ErrorResponse
            {
                HttpStatus = 403,
                ResponseHttpStatus = 200,
                ResponsePagePath = "/index.html",
            },
        ];

        _ = new PrivateStaticAssetsConstruct(stack, "assets", props);

        var template = Template.FromStack(stack);
        template.HasResourceProperties("AWS::CloudFront::Distribution", Match.ObjectLike(new Dictionary<string, object>
        {
            ["DistributionConfig"] = Match.ObjectLike(new Dictionary<string, object>
            {
                ["CustomErrorResponses"] = Match.ArrayWith(new object[]
                {
                    Match.ObjectLike(new Dictionary<string, object>
                    {
                        ["ErrorCode"] = 403,
                        ["ResponseCode"] = 200,
                        ["ResponsePagePath"] = "/index.html",
                    }),
                }),
            }),
        }));
    }

    [Fact]
    public void Construct_ShouldSetDefaultRootObject()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        var props = CreateProps();
        props.DefaultRootObject = "index.html";

        _ = new PrivateStaticAssetsConstruct(stack, "assets", props);

        var template = Template.FromStack(stack);
        template.HasResourceProperties("AWS::CloudFront::Distribution", Match.ObjectLike(new Dictionary<string, object>
        {
            ["DistributionConfig"] = Match.ObjectLike(new Dictionary<string, object>
            {
                ["DefaultRootObject"] = "index.html",
            }),
        }));
    }

    [Fact]
    public void Construct_ShouldSetComment()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        var props = CreateProps();
        props.Comment = "Private game assets";

        _ = new PrivateStaticAssetsConstruct(stack, "assets", props);

        var template = Template.FromStack(stack);
        template.HasResourceProperties("AWS::CloudFront::Distribution", Match.ObjectLike(new Dictionary<string, object>
        {
            ["DistributionConfig"] = Match.ObjectLike(new Dictionary<string, object>
            {
                ["Comment"] = "Private game assets",
            }),
        }));
    }

    // ---- Immutable deployment ----

    [Fact]
    public void Construct_ShouldNotPruneByDefault()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();

        _ = new PrivateStaticAssetsConstruct(stack, "assets", CreateProps());

        var template = Template.FromStack(stack);
        template.HasResourceProperties("Custom::CDKBucketDeployment", Match.ObjectLike(new Dictionary<string, object>
        {
            ["Prune"] = false,
        }));
    }

    [Fact]
    public void Construct_ShouldPruneWhenConfigured()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        var props = CreateProps();
        props.Prune = true;

        _ = new PrivateStaticAssetsConstruct(stack, "assets", props);

        var template = Template.FromStack(stack);
        template.HasResourceProperties("Custom::CDKBucketDeployment", Match.ObjectLike(new Dictionary<string, object>
        {
            ["Prune"] = true,
        }));
    }

    [Fact]
    public void Construct_ShouldApplyDestinationKeyPrefix()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        var props = CreateProps();
        props.DestinationKeyPrefix = "game/abc123/";

        _ = new PrivateStaticAssetsConstruct(stack, "assets", props);

        var template = Template.FromStack(stack);
        template.HasResourceProperties("Custom::CDKBucketDeployment", Match.ObjectLike(new Dictionary<string, object>
        {
            ["DestinationBucketKeyPrefix"] = "game/abc123/",
        }));
    }

    [Fact]
    public void Construct_ShouldRetainDeploymentByDefault()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();

        _ = new PrivateStaticAssetsConstruct(stack, "assets", CreateProps());

        var template = Template.FromStack(stack);
        template.HasResourceProperties("Custom::CDKBucketDeployment", Match.ObjectLike(new Dictionary<string, object>
        {
            ["RetainOnDelete"] = true,
        }));
    }

    [Fact]
    public void Construct_ShouldHonorRetainOnDeleteFalse()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        var props = CreateProps();
        props.RetainOnDelete = false;

        _ = new PrivateStaticAssetsConstruct(stack, "assets", props);

        var template = Template.FromStack(stack);
        template.HasResourceProperties("Custom::CDKBucketDeployment", Match.ObjectLike(new Dictionary<string, object>
        {
            ["RetainOnDelete"] = false,
        }));
    }

    [Fact]
    public void Construct_ShouldNotApplyCacheControlByDefault()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();

        _ = new PrivateStaticAssetsConstruct(stack, "assets", CreateProps());

        var template = Template.FromStack(stack);
        template.HasResourceProperties("Custom::CDKBucketDeployment", Match.Not(Match.ObjectLike(new Dictionary<string, object>
        {
            ["SystemMetadata"] = Match.AnyValue(),
        })));
    }

    [Fact]
    public void Construct_ShouldApplyCacheControl()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        var props = CreateProps();
        props.CacheControl = [CacheControl.SetPublic(), CacheControl.MaxAge(Duration.Days(365)), CacheControl.Immutable()];

        _ = new PrivateStaticAssetsConstruct(stack, "assets", props);

        var template = Template.FromStack(stack);
        template.HasResourceProperties("Custom::CDKBucketDeployment", Match.ObjectLike(new Dictionary<string, object>
        {
            ["SystemMetadata"] = Match.ObjectLike(new Dictionary<string, object>
            {
                ["cache-control"] = "public, max-age=31536000, immutable",
            }),
        }));
    }

    [Fact]
    public void Construct_ShouldNotInvalidateWhenNoPathsAreSet()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();

        _ = new PrivateStaticAssetsConstruct(stack, "assets", CreateProps());

        var template = Template.FromStack(stack);
        template.HasResourceProperties("Custom::CDKBucketDeployment", Match.Not(Match.ObjectLike(new Dictionary<string, object>
        {
            ["DistributionId"] = Match.AnyValue(),
        })));
        template.HasResourceProperties("Custom::CDKBucketDeployment", Match.Not(Match.ObjectLike(new Dictionary<string, object>
        {
            ["DistributionPaths"] = Match.AnyValue(),
        })));
    }

    [Fact]
    public void Construct_ShouldInvalidateWhenPathsAreSet()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        var props = CreateProps();
        props.InvalidationPaths = ["/index.html", "/assets/*"];

        _ = new PrivateStaticAssetsConstruct(stack, "assets", props);

        var template = Template.FromStack(stack);
        template.HasResourceProperties("Custom::CDKBucketDeployment", Match.ObjectLike(new Dictionary<string, object>
        {
            ["DistributionId"] = Match.AnyValue(),
            ["DistributionPaths"] = Match.ArrayWith(new object[] { "/index.html", "/assets/*" }),
        }));
    }

    // ---- Custom DNS (off by default) ----

    [Fact]
    public void Construct_ShouldNotConfigureDnsByDefault()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();

        var construct = new PrivateStaticAssetsConstruct(stack, "assets", CreateProps());

        var template = Template.FromStack(stack);
        template.ResourceCountIs("AWS::CertificateManager::Certificate", 0);
        template.ResourceCountIs("AWS::Route53::RecordSet", 0);
        template.HasResourceProperties("AWS::CloudFront::Distribution", Match.Not(Match.ObjectLike(new Dictionary<string, object>
        {
            ["DistributionConfig"] = Match.ObjectLike(new Dictionary<string, object>
            {
                ["Aliases"] = Match.AnyValue(),
            }),
        })));

        construct.DomainName.Should().Be(construct.Distribution.DistributionDomainName);
    }

    [Fact]
    public void Construct_ShouldConfigureCustomDomainWithCertificateAndRecords()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();

        var zone = HostedZone.FromHostedZoneAttributes(stack, "HostedZone", new HostedZoneAttributes
        {
            HostedZoneId = "Z1234567890ABC",
            ZoneName = "example.com",
        });

        var certificate = Certificate.FromCertificateArn(
            stack,
            "Certificate",
            "arn:aws:acm:us-east-1:123456789012:certificate/11111111-2222-3333-4444-555555555555");

        var props = CreateProps();
        props.Domain = new PrivateStaticAssetsDomainOptions
        {
            DomainNames = ["assets.example.com", "cdn.example.com"],
            Certificate = certificate,
            HostedZone = zone,
        };

        var construct = new PrivateStaticAssetsConstruct(stack, "assets", props);

        var template = Template.FromStack(stack);
        template.HasResourceProperties("AWS::CloudFront::Distribution", Match.ObjectLike(new Dictionary<string, object>
        {
            ["DistributionConfig"] = Match.ObjectLike(new Dictionary<string, object>
            {
                ["Aliases"] = Match.ArrayWith(new object[] { "assets.example.com", "cdn.example.com" }),
                ["ViewerCertificate"] = Match.ObjectLike(new Dictionary<string, object>
                {
                    ["AcmCertificateArn"] = "arn:aws:acm:us-east-1:123456789012:certificate/11111111-2222-3333-4444-555555555555",
                }),
            }),
        }));

        template.ResourceCountIs("AWS::Route53::RecordSet", 4);
        template.HasResourceProperties("AWS::Route53::RecordSet", Match.ObjectLike(new Dictionary<string, object>
        {
            ["Name"] = "assets.example.com.",
            ["Type"] = "A",
        }));
        template.HasResourceProperties("AWS::Route53::RecordSet", Match.ObjectLike(new Dictionary<string, object>
        {
            ["Name"] = "assets.example.com.",
            ["Type"] = "AAAA",
        }));
        template.HasResourceProperties("AWS::Route53::RecordSet", Match.ObjectLike(new Dictionary<string, object>
        {
            ["Name"] = "cdn.example.com.",
            ["Type"] = "A",
        }));
        template.HasResourceProperties("AWS::Route53::RecordSet", Match.ObjectLike(new Dictionary<string, object>
        {
            ["Name"] = "cdn.example.com.",
            ["Type"] = "AAAA",
        }));

        construct.DomainName.Should().Be("assets.example.com");
    }

    [Fact]
    public void Construct_ShouldCreateDnsValidatedCertificateWhenCertificateOmitted()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();

        var zone = HostedZone.FromHostedZoneAttributes(stack, "HostedZone", new HostedZoneAttributes
        {
            HostedZoneId = "Z1234567890ABC",
            ZoneName = "example.com",
        });

        var props = CreateProps();
        props.Domain = new PrivateStaticAssetsDomainOptions
        {
            DomainNames = ["assets.example.com", "cdn.example.com"],
            HostedZone = zone,
        };

        _ = new PrivateStaticAssetsConstruct(stack, "assets", props);

        var template = Template.FromStack(stack);
        template.HasResourceProperties("AWS::CertificateManager::Certificate", Match.ObjectLike(new Dictionary<string, object>
        {
            ["DomainName"] = "assets.example.com",
            ["SubjectAlternativeNames"] = Match.ArrayWith(new object[] { "cdn.example.com" }),
            ["ValidationMethod"] = "DNS",
        }));
    }

    [Fact]
    public void Construct_ShouldLookUpHostedZoneByName()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();

        var props = CreateProps();
        props.Domain = new PrivateStaticAssetsDomainOptions
        {
            DomainNames = ["assets.example.com"],
            HostedZoneName = "example.com",
        };

        _ = new PrivateStaticAssetsConstruct(stack, "assets", props);

        var template = Template.FromStack(stack);
        template.HasResourceProperties("AWS::CertificateManager::Certificate", Match.ObjectLike(new Dictionary<string, object>
        {
            ["DomainName"] = "assets.example.com",
            ["ValidationMethod"] = "DNS",
        }));
        template.ResourceCountIs("AWS::Route53::RecordSet", 2);
        template.HasResourceProperties("AWS::Route53::RecordSet", Match.ObjectLike(new Dictionary<string, object>
        {
            ["Name"] = "assets.example.com.",
            ["Type"] = "A",
        }));
        template.HasResourceProperties("AWS::Route53::RecordSet", Match.ObjectLike(new Dictionary<string, object>
        {
            ["Name"] = "assets.example.com.",
            ["Type"] = "AAAA",
        }));
    }

    // ---- DNS validation ----

    [Fact]
    public void Construct_ShouldThrowWhenDomainHasNoNames()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        var props = CreateProps();
        props.Domain = new PrivateStaticAssetsDomainOptions { DomainNames = [] };

        var act = () => new PrivateStaticAssetsConstruct(stack, "assets", props);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Construct_ShouldThrowWhenBothZoneOptionsAreSet()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        var zone = HostedZone.FromHostedZoneAttributes(stack, "HostedZone", new HostedZoneAttributes
        {
            HostedZoneId = "Z1234567890ABC",
            ZoneName = "example.com",
        });
        var props = CreateProps();
        props.Domain = new PrivateStaticAssetsDomainOptions
        {
            DomainNames = ["assets.example.com"],
            HostedZone = zone,
            HostedZoneName = "example.com",
        };

        var act = () => new PrivateStaticAssetsConstruct(stack, "assets", props);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Construct_ShouldThrowWhenNoCertificateAndNoZone()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        var props = CreateProps();
        props.Domain = new PrivateStaticAssetsDomainOptions
        {
            DomainNames = ["assets.example.com"],
        };

        var act = () => new PrivateStaticAssetsConstruct(stack, "assets", props);

        act.Should().Throw<ArgumentException>();
    }

    // ---- Public properties and composed props ----

    [Fact]
    public void Construct_ShouldExposePublicProperties()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();

        var construct = new PrivateStaticAssetsConstruct(stack, "assets", CreateProps());

        construct.Bucket.Should().NotBeNull();
        construct.Distribution.Should().NotBeNull();
        construct.DomainName.Should().NotBeNullOrEmpty();
    }

    [Theory]
    [Compose<PrivateStaticAssetsConstructPropsProfile>]
    public void Construct_WithComposedProps_ShouldCreatePrivateAssets(PrivateStaticAssetsConstructProps props)
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();

        _ = new PrivateStaticAssetsConstruct(stack, "assets", props);

        var template = Template.FromStack(stack);
        template.ResourceCountIs("AWS::S3::Bucket", 1);
        template.HasResourceProperties("AWS::S3::Bucket", Match.ObjectLike(new Dictionary<string, object>
        {
            ["PublicAccessBlockConfiguration"] = Match.ObjectLike(new Dictionary<string, object>
            {
                ["BlockPublicAcls"] = true,
                ["BlockPublicPolicy"] = true,
                ["IgnorePublicAcls"] = true,
                ["RestrictPublicBuckets"] = true,
            }),
        }));
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
}

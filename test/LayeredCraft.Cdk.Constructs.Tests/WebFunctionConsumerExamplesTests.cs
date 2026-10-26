using System.Collections.Generic;
using Amazon.CDK;
using Amazon.CDK.AWS.CertificateManager;
using Amazon.CDK.AWS.CloudFront;
using Amazon.CDK.AWS.CloudFront.Origins;
using Amazon.CDK.AWS.DynamoDB;
using Amazon.CDK.AWS.IAM;
using Amazon.CDK.AWS.Route53;
using Amazon.CDK.AWS.Route53.Targets;
using Amazon.CDK.AWS.S3;
using Amazon.CDK.Assertions;
using AwesomeAssertions;
using LayeredCraft.Cdk.Constructs;
using LayeredCraft.Cdk.Constructs.Models;
using LayeredCraft.Cdk.Constructs.Testing;

namespace LayeredCraft.Cdk.Constructs.Tests;

/// <summary>
/// Compiles and synthesizes the consumer examples from the approved design (§9.1 - §9.7)
/// using only the public API, so the documented usage keeps working.
/// </summary>
[Collection("CDK Tests")]
public class WebFunctionConsumerExamplesTests
{
    private static int _artifactsId;

    private static IBucket ImportedArtifacts(Stack stack, string? id = null) =>
        Bucket.FromBucketName(stack, id ?? $"Artifacts{++_artifactsId}", "acme-artifacts");

    [Fact]
    public void Example91_AspNetCoreApi_ShouldSynthesize()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        var artifacts = ImportedArtifacts(stack);

        // The artifacts bucket is owned by another stack, so its read policy is ExternallyManaged: the bucket
        // owner grants lambda.amazonaws.com read on web-function/acme-*/revision/* (design §9.1).
        var table = new Table(stack, "Table", new TableProps
        {
            PartitionKey = new Amazon.CDK.AWS.DynamoDB.Attribute { Name = "pk", Type = AttributeType.STRING },
            BillingMode = BillingMode.PAY_PER_REQUEST,
        });

        var api = new WebFunctionConstruct(stack, "api", new WebFunctionConstructProps
        {
            FunctionName = "acme-api",
            Code = WebFunctionCode.FromBucket(
                artifacts,
                "1.2.3/api.zip",
                new WebFunctionCodeFromBucketOptions { BucketPolicy = WebFunctionCodeBucketPolicy.ExternallyManaged }),
            EnvironmentVariables = new Dictionary<string, string>
            {
                ["ASPNETCORE_ENVIRONMENT"] = "Production",
                ["Acme__TableName"] = table.TableName,
            },
            MaxConcurrencyPerEnvironment = 16,
            Endpoint = new WebFunctionEndpointOptions
            {
                AuthType = WebFunctionAuthType.IamAuth,
                MaxEnvironments = 10,
                RateLimit = 100,
            },
        });

        table.GrantReadWriteData(api);

        var template = Template.FromStack(stack);
        template.ShouldHaveWebFunction("acme-api");
        template.ShouldHaveEndpoint("acme-api", "default", WebFunctionAuthType.IamAuth);
        template.ShouldHaveEndpointScaling("default", 10, 100);
    }

    [Fact]
    public void Example92_NextJs_ShouldSynthesize()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        var codeBucket = new Bucket(stack, "CodeBucket", new BucketProps { Versioned = true });

        var api = new WebFunctionConstruct(stack, "api", new WebFunctionConstructProps
        {
            FunctionName = "acme-api",
            Code = WebFunctionCode.FromBucket(
                ImportedArtifacts(stack),
                "api.zip",
                new WebFunctionCodeFromBucketOptions { BucketPolicy = WebFunctionCodeBucketPolicy.ExternallyManaged }),
            Endpoint = new WebFunctionEndpointOptions { AuthType = WebFunctionAuthType.IamAuth },
        });

        var web = new WebFunctionConstruct(stack, "web", new WebFunctionConstructProps
        {
            FunctionName = "acme-web",
            Code = WebFunctionCode.FromBucket(
                codeBucket,
                "web.zip",
                new WebFunctionCodeFromBucketOptions { ObjectVersion = "version-1" }),
            EnvironmentVariables = new Dictionary<string, string>
            {
                ["NODE_ENV"] = "production",
                ["NEXT_TELEMETRY_DISABLED"] = "1",
                ["ACME_API_URL"] = api.DefaultEndpoint.Url,
            },
            Endpoint = new WebFunctionEndpointOptions { AuthType = WebFunctionAuthType.IamAuth },
        });

        var template = Template.FromStack(stack);
        template.ShouldHaveWebFunction("acme-web");
        template.ShouldHaveRevisionCode("acme-artifacts", "api.zip");
        // The owned code bucket's name is a token, so assert the pinned object version directly.
        template.HasResourceProperties("AWS::Lambda::WebFunctionRevision", Match.ObjectLike(new Dictionary<string, object>
        {
            ["BuildConfig"] = Match.ObjectLike(new Dictionary<string, object>
            {
                ["CodeConfig"] = Match.ObjectLike(new Dictionary<string, object>
                {
                    ["S3Object"] = Match.ObjectLike(new Dictionary<string, object>
                    {
                        ["Key"] = "web.zip",
                        ["VersionId"] = "version-1",
                    }),
                }),
            }),
        }));
        template.ShouldHaveCodeBucketReadStatement("acme-web");
    }

    [Fact]
    public void Example93_NextJsInvokeGrant_ShouldSynthesize()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();

        var api = new WebFunctionConstruct(stack, "api", new WebFunctionConstructProps
        {
            FunctionName = "acme-api",
            Code = WebFunctionCode.FromBucket(
                ImportedArtifacts(stack),
                "api.zip",
                new WebFunctionCodeFromBucketOptions { BucketPolicy = WebFunctionCodeBucketPolicy.ExternallyManaged }),
            Endpoint = new WebFunctionEndpointOptions { AuthType = WebFunctionAuthType.IamAuth },
        });

        var web = new WebFunctionConstruct(stack, "web", new WebFunctionConstructProps
        {
            FunctionName = "acme-web",
            Code = WebFunctionCode.FromBucket(
                ImportedArtifacts(stack),
                "web.zip",
                new WebFunctionCodeFromBucketOptions { BucketPolicy = WebFunctionCodeBucketPolicy.ExternallyManaged }),
            Endpoint = new WebFunctionEndpointOptions { AuthType = WebFunctionAuthType.IamAuth },
        });

        // Same account: an identity policy on the caller, no resource policy.
        api.DefaultEndpoint.GrantInvoke(web);

        // Cross-account caller: the caller policy is ExternallyManaged, so only the endpoint's resource-policy
        // statement is written here. Invocation works only after account 222233334444 attaches an identity
        // policy allowing lambda:InvokeWebFunctionEndpoint on this endpoint ARN (design §9.3).
        var partner = Role.FromRoleArn(stack, "Partner", "arn:aws:iam::222233334444:role/partner");
        api.DefaultEndpoint.GrantInvoke(partner, new WebFunctionInvokeGrantOptions
        {
            CallerPolicy = WebFunctionCallerPolicy.ExternallyManaged,
        });

        var template = Template.FromStack(stack);
        template.ShouldHaveInvokeGrant("acme-api", "default");
        template.ShouldHaveSingleResourcePolicy("acme-api");
        template.ShouldAllowResourcePolicyPrincipal("acme-api", "default", "arn:aws:iam::222233334444:role/partner");
    }

    [Fact]
    public void Example94_CloudFrontOac_ShouldSynthesize()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();

        var web = new WebFunctionConstruct(stack, "web", new WebFunctionConstructProps
        {
            FunctionName = "acme-web",
            Code = WebFunctionCode.FromBucket(
                ImportedArtifacts(stack),
                "web.zip",
                new WebFunctionCodeFromBucketOptions { BucketPolicy = WebFunctionCodeBucketPolicy.ExternallyManaged }),
            Endpoint = new WebFunctionEndpointOptions { AuthType = WebFunctionAuthType.IamAuth },
        });

        var certificate = Certificate.FromCertificateArn(
            stack, "Certificate", "arn:aws:acm:us-east-1:123456789012:certificate/11111111-1111-1111-1111-111111111111");

        var origin = WebFunctionOrigin.WithOriginAccessControl(web.DefaultEndpoint, new WebFunctionOriginWithOacProps
        {
            CustomHeaders = new Dictionary<string, string> { ["x-origin-verify"] = "secret" },
        });

        _ = new Distribution(stack, "distribution", new DistributionProps
        {
            DefaultBehavior = new BehaviorOptions
            {
                Origin = origin,
                ViewerProtocolPolicy = ViewerProtocolPolicy.REDIRECT_TO_HTTPS,
                AllowedMethods = AllowedMethods.ALLOW_ALL,
                CachePolicy = CachePolicy.CACHING_DISABLED,
                OriginRequestPolicy = OriginRequestPolicy.ALL_VIEWER_EXCEPT_HOST_HEADER,
            },
            AdditionalBehaviors = new Dictionary<string, IBehaviorOptions>
            {
                ["/_next/static/*"] = new BehaviorOptions
                {
                    Origin = origin,
                    CachePolicy = CachePolicy.CACHING_OPTIMIZED,
                },
            },
            DomainNames = ["app.example.com"],
            Certificate = certificate,
        });

        var template = Template.FromStack(stack);
        template.ShouldHaveWebFunctionOac("sigv4");
        template.ShouldAllowCloudFront("acme-web", "default");
    }

    [Fact]
    public void Example95_CustomDomainAndRoute53_ShouldSynthesize()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();

        var zone = HostedZone.FromHostedZoneAttributes(stack, "Zone", new HostedZoneAttributes
        {
            HostedZoneId = "Z1234567890ABC",
            ZoneName = "example.com",
        });

        var web = new WebFunctionConstruct(stack, "web", new WebFunctionConstructProps
        {
            FunctionName = "acme-web",
            Code = WebFunctionCode.FromBucket(
                ImportedArtifacts(stack),
                "web.zip",
                new WebFunctionCodeFromBucketOptions { BucketPolicy = WebFunctionCodeBucketPolicy.ExternallyManaged }),
            Endpoint = new WebFunctionEndpointOptions { AuthType = WebFunctionAuthType.IamAuth },
        });

        var certificate = new Certificate(stack, "Certificate", new CertificateProps
        {
            DomainName = "app.example.com",
            Validation = CertificateValidation.FromDns(zone),
        });

        var distribution = new Distribution(stack, "distribution", new DistributionProps
        {
            DefaultBehavior = new BehaviorOptions
            {
                Origin = WebFunctionOrigin.WithOriginAccessControl(web.DefaultEndpoint),
            },
            DomainNames = ["app.example.com"],
            Certificate = certificate,
        });

        _ = new ARecord(stack, "Alias", new ARecordProps
        {
            Zone = zone,
            RecordName = "app",
            Target = RecordTarget.FromAlias(new CloudFrontTarget(distribution)),
        });
        _ = new AaaaRecord(stack, "AliasV6", new AaaaRecordProps
        {
            Zone = zone,
            RecordName = "app",
            Target = RecordTarget.FromAlias(new CloudFrontTarget(distribution)),
        });

        var template = Template.FromStack(stack);
        template.ShouldHaveWebFunction("acme-web");
        template.ResourceCountIs("AWS::Route53::RecordSet", 2);
    }

    [Fact]
    public void Example96_PublicApiInternalAndMultiEndpoint_ShouldSynthesize()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();

        // (a) ApplicationManaged public API.
        var publicApi = new WebFunctionConstruct(stack, "publicApi", new WebFunctionConstructProps
        {
            FunctionName = "public-api",
            Code = WebFunctionCode.FromBucket(ImportedArtifacts(stack), "public-api.zip",
                new WebFunctionCodeFromBucketOptions { BucketPolicy = WebFunctionCodeBucketPolicy.ExternallyManaged }),
            Endpoint = new WebFunctionEndpointOptions { AuthType = WebFunctionAuthType.ApplicationManaged },
        });

        // (b) IamAuth internal endpoint invoked by an existing Lambda. Same account, so GrantInvoke writes an
        // identity policy on the caller's role and no resource policy on the endpoint.
        var worker = new WebFunctionConstruct(stack, "worker", new WebFunctionConstructProps
        {
            FunctionName = "worker",
            Code = WebFunctionCode.FromBucket(ImportedArtifacts(stack, "ArtifactsWorker"), "worker.zip",
                new WebFunctionCodeFromBucketOptions { BucketPolicy = WebFunctionCodeBucketPolicy.ExternallyManaged }),
            Endpoint = new WebFunctionEndpointOptions { AuthType = WebFunctionAuthType.IamAuth },
        });
        var existingLambda = new LambdaFunctionConstruct(stack, "existing", new LambdaFunctionConstructProps
        {
            FunctionName = "existing-caller",
            FunctionSuffix = "existing",
            AssetPath = CdkTestHelper.GetTestLambdaZipPath(),
            RoleName = "existing-caller-role",
            PolicyName = "existing-caller-policy",
        });
        worker.DefaultEndpoint.GrantInvoke(existingLambda.LambdaFunction);

        // (c) Two endpoints with different auth.
        var web = new WebFunctionConstruct(stack, "web", new WebFunctionConstructProps
        {
            FunctionName = "acme-web",
            Code = WebFunctionCode.FromBucket(ImportedArtifacts(stack, "ArtifactsWeb"), "web.zip",
                new WebFunctionCodeFromBucketOptions { BucketPolicy = WebFunctionCodeBucketPolicy.ExternallyManaged }),
            Endpoint = new WebFunctionEndpointOptions { AuthType = WebFunctionAuthType.IamAuth },
        });
        web.AddEndpoint("webhooks", new WebFunctionEndpointOptions
        {
            EndpointName = "webhooks",
            AuthType = WebFunctionAuthType.ApplicationManaged,
            RateLimit = 100,
        });

        var template = Template.FromStack(stack);
        template.ShouldHaveEndpoint("public-api", "default", WebFunctionAuthType.ApplicationManaged);
        template.ShouldHaveEndpoint("acme-web", "default", WebFunctionAuthType.IamAuth);
        template.ShouldHaveEndpoint("acme-web", "webhooks", WebFunctionAuthType.ApplicationManaged);
        template.ShouldHaveInvokeGrant("worker", "default");
    }

    [Fact]
    public void Example97_ImportedEndpoint_ShouldSynthesize()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();

        var shared = WebFunctionEndpointConstruct.FromEndpointAttributes(stack, "shared", new WebFunctionEndpointAttributes
        {
            EndpointArn = "arn:aws:lambda:us-east-1:111122223333:web-function/shared/endpoint/default",
            DomainName = "abc123.lambda-web.us-east-1.on.aws",
            AuthType = WebFunctionAuthType.IamAuth,
        });

        // The endpoint is owned by another stack, so origin permissions are ExternallyManaged: the owner adds the
        // CloudFront aws:SourceArn statement. The origin still synthesizes and no resource policy is created here.
        var origin = WebFunctionOrigin.WithOriginAccessControl(shared, new WebFunctionOriginWithOacProps
        {
            Permissions = WebFunctionOriginPermissions.ExternallyManaged,
        });

        _ = new Distribution(stack, "distribution", new DistributionProps
        {
            DefaultBehavior = new BehaviorOptions { Origin = origin },
        });

        var template = Template.FromStack(stack);
        template.ShouldHaveWebFunctionOac("sigv4");
        template.ShouldHaveNoResourcePolicy();
    }
}

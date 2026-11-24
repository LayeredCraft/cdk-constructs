using Amazon.CDK;
using Amazon.CDK.AWS.CertificateManager;
using Amazon.CDK.AWS.CloudFront;
using Amazon.CDK.AWS.Route53;
using Amazon.CDK.AWS.S3.Deployment;
using Amazon.CDK.Assertions;
using AwesomeAssertions;
using LayeredCraft.Cdk.Constructs;
using LayeredCraft.Cdk.Constructs.Models;
using LayeredCraft.Cdk.Constructs.Testing;

namespace LayeredCraft.Cdk.Constructs.Tests.Testing;

/// <summary>
/// Verifies the consumer-facing <see cref="PrivateStaticAssetsConstructAssertions"/> and
/// <see cref="PrivateStaticAssetsConstructPropsBuilder"/> helpers, including negative cases that ensure a wrong
/// expectation is actually detected.
/// </summary>
[Collection("CDK Tests")]
public class PrivateStaticAssetsConstructTestingHelpersTests
{
    private const string AssetPath = "TestAssets/static-site";

    private static PrivateStaticAssetsConstruct CreateConstruct(
        Stack stack,
        PrivateStaticAssetsConstructProps props,
        string id = "assets")
        => new(stack, id, props);

    private static PrivateStaticAssetsConstructProps DefaultProps() => new()
    {
        AssetPath = CdkTestHelper.GetTestAssetPath(AssetPath),
    };

    // ---- ShouldHavePrivateBucket ----

    [Fact]
    public void ShouldHavePrivateBucket_ShouldPassForTheConstructBucket()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        _ = CreateConstruct(stack, DefaultProps());

        var act = () => Template.FromStack(stack).ShouldHavePrivateBucket();

        act.Should().NotThrow();
    }

    [Fact]
    public void ShouldHavePrivateBucket_ShouldThrowWhenNoBucketExists()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();

        var act = () => Template.FromStack(stack).ShouldHavePrivateBucket();

        act.Should().Throw<Exception>();
    }

    // ---- ShouldNotHaveWebsiteEndpoint ----

    [Fact]
    public void ShouldNotHaveWebsiteEndpoint_ShouldPassForTheConstructBucket()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        _ = CreateConstruct(stack, DefaultProps());

        var act = () => Template.FromStack(stack).ShouldNotHaveWebsiteEndpoint();

        act.Should().NotThrow();
    }

    // ---- ShouldHaveOacOnlyOrigin ----

    [Fact]
    public void ShouldHaveOacOnlyOrigin_ShouldPass()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        _ = CreateConstruct(stack, DefaultProps());

        var act = () => Template.FromStack(stack).ShouldHaveOacOnlyOrigin();

        act.Should().NotThrow();
    }

    [Fact]
    public void ShouldHaveOacOnlyOrigin_ShouldThrowWhenNoOriginAccessControlExists()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();

        var act = () => Template.FromStack(stack).ShouldHaveOacOnlyOrigin();

        act.Should().Throw<Exception>();
    }

    // ---- ShouldScopeBucketPolicyToDistribution ----

    [Fact]
    public void ShouldScopeBucketPolicyToDistribution_ShouldPass()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        _ = CreateConstruct(stack, DefaultProps());

        var act = () => Template.FromStack(stack).ShouldScopeBucketPolicyToDistribution();

        act.Should().NotThrow();
    }

    // ---- ShouldHaveHttpsOnlyViewerPolicy ----

    [Fact]
    public void ShouldHaveHttpsOnlyViewerPolicy_ShouldPassByDefault()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        _ = CreateConstruct(stack, DefaultProps());

        var act = () => Template.FromStack(stack).ShouldHaveHttpsOnlyViewerPolicy();

        act.Should().NotThrow();
    }

    [Fact]
    public void ShouldHaveHttpsOnlyViewerPolicy_ShouldThrowForRedirectPolicy()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        var props = DefaultProps();
        props.ViewerProtocolPolicy = ViewerProtocolPolicy.REDIRECT_TO_HTTPS;
        _ = CreateConstruct(stack, props);

        var act = () => Template.FromStack(stack).ShouldHaveHttpsOnlyViewerPolicy();

        act.Should().Throw<Exception>();
    }

    // ---- ShouldHaveBucketDeployment ----

    [Fact]
    public void ShouldHaveBucketDeployment_ShouldMatchSettings()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        var props = DefaultProps();
        props.DestinationKeyPrefix = "game/abc123/";
        props.CacheControl = [CacheControl.SetPublic(), CacheControl.MaxAge(Duration.Days(365)), CacheControl.Immutable()];
        _ = CreateConstruct(stack, props);

        var act = () => Template.FromStack(stack).ShouldHaveBucketDeployment(
            "game/abc123/", prune: false, retainOnDelete: true, cacheControl: "public, max-age=31536000, immutable");

        act.Should().NotThrow();
    }

    [Fact]
    public void ShouldHaveBucketDeployment_ShouldThrowForWrongPruneFlag()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        _ = CreateConstruct(stack, DefaultProps());

        var act = () => Template.FromStack(stack).ShouldHaveBucketDeployment(prune: true);

        act.Should().Throw<Exception>();
    }

    // ---- ShouldHaveDnsRecords ----

    [Fact]
    public void ShouldHaveDnsRecords_ShouldMatchConfiguredDomains()
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
        var props = DefaultProps();
        props.Domain = new PrivateStaticAssetsDomainOptions
        {
            DomainNames = ["assets.example.com", "cdn.example.com"],
            Certificate = certificate,
            HostedZone = zone,
        };
        _ = CreateConstruct(stack, props);

        var act = () => Template.FromStack(stack).ShouldHaveDnsRecords("assets.example.com", "cdn.example.com");

        act.Should().NotThrow();
    }

    [Fact]
    public void ShouldHaveDnsRecords_ShouldThrowForUnknownDomain()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        _ = CreateConstruct(stack, DefaultProps());

        var act = () => Template.FromStack(stack).ShouldHaveDnsRecords("missing.example.com");

        act.Should().Throw<Exception>();
    }

    // ---- PrivateStaticAssetsConstructPropsBuilder ----

    [Fact]
    public void PropsBuilder_ShouldApplyDocumentedDefaults()
    {
        var props = new PrivateStaticAssetsConstructPropsBuilder().Build();

        props.AssetPath.Should().EndWith("TestAssets/static-site");
        Path.IsPathRooted(props.AssetPath).Should().BeTrue();
        props.DestinationKeyPrefix.Should().BeNull();
        props.Prune.Should().BeFalse();
        props.RetainOnDelete.Should().BeTrue();
        props.CacheControl.Should().BeEmpty();
        props.InvalidationPaths.Should().BeNull();
        props.RemovalPolicy.Should().Be(RemovalPolicy.RETAIN);
        props.ViewerProtocolPolicy.Should().Be(ViewerProtocolPolicy.HTTPS_ONLY);
        props.CachePolicy.CachePolicyId.Should().Be("658327ea-f89d-4fab-a63d-7e88639e58f6");
        props.Comment.Should().BeNull();
        props.SpaRouting.Should().BeFalse();
        props.ErrorResponses.Should().BeEmpty();
        props.DefaultRootObject.Should().BeNull();
        props.Domain.Should().BeNull();
    }

    [Fact]
    public void PropsBuilder_ShouldFlowConfiguredValues()
    {
        var errorResponse = new ErrorResponse { HttpStatus = 404, ResponsePagePath = "/index.html" };

        var props = new PrivateStaticAssetsConstructPropsBuilder()
            .WithAssetPath(CdkTestHelper.GetTestAssetPath("TestAssets/static-site"))
            .WithDestinationKeyPrefix("game/abc123/")
            .WithPrune()
            .WithRetainOnDelete(false)
            .WithCacheControl(CacheControl.Immutable())
            .WithInvalidationPaths("/index.html")
            .WithRemovalPolicy(RemovalPolicy.DESTROY)
            .WithViewerProtocolPolicy(ViewerProtocolPolicy.REDIRECT_TO_HTTPS)
            .WithCachePolicy(CachePolicy.CACHING_DISABLED)
            .WithComment("assets")
            .WithSpaRouting()
            .WithErrorResponse(errorResponse)
            .WithDefaultRootObject("index.html")
            .Build();

        props.DestinationKeyPrefix.Should().Be("game/abc123/");
        props.Prune.Should().BeTrue();
        props.RetainOnDelete.Should().BeFalse();
        props.CacheControl.Should().HaveCount(1);
        props.InvalidationPaths.Should().Equal("/index.html");
        props.RemovalPolicy.Should().Be(RemovalPolicy.DESTROY);
        props.ViewerProtocolPolicy.Should().Be(ViewerProtocolPolicy.REDIRECT_TO_HTTPS);
        props.CachePolicy.CachePolicyId.Should().Be("4135ea2d-6df8-44a3-9df3-4b5a84be39ad");
        props.Comment.Should().Be("assets");
        props.SpaRouting.Should().BeTrue();
        props.ErrorResponses.Should().ContainSingle().Which.Should().BeSameAs(errorResponse);
        props.DefaultRootObject.Should().Be("index.html");
    }

    [Fact]
    public void PropsBuilder_ShouldCreateValidConstruct()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack();
        var props = new PrivateStaticAssetsConstructPropsBuilder()
            .WithDestinationKeyPrefix("game/abc123/")
            .WithCacheControl(CacheControl.SetPublic(), CacheControl.MaxAge(Duration.Days(365)), CacheControl.Immutable())
            .Build();

        _ = new PrivateStaticAssetsConstruct(stack, "assets", props);

        var template = Template.FromStack(stack);
        template.ShouldHavePrivateBucket();
        template.ShouldHaveOacOnlyOrigin();
        template.ShouldHaveHttpsOnlyViewerPolicy();
        template.ShouldHaveBucketDeployment(
            "game/abc123/", prune: false, retainOnDelete: true, cacheControl: "public, max-age=31536000, immutable");
    }
}

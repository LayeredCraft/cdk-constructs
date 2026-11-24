using Amazon.CDK;
using Amazon.CDK.AWS.CertificateManager;
using Amazon.CDK.AWS.Route53;
using Amazon.CDK.AWS.S3.Deployment;
using LayeredCraft.Cdk.Constructs.Models;
using LayeredCraft.Cdk.Constructs.Testing;
using LayeredCraft.Cdk.Constructs.Tests.TestKit.Golden;

namespace LayeredCraft.Cdk.Constructs.Tests;

/// <summary>
/// Golden-template compatibility tests for <see cref="PrivateStaticAssetsConstruct"/>. Each case pins the
/// complete synthesized CloudFormation template for a representative configuration: the default case and a
/// custom-domain case that also exercises the immutable deployment options.
/// </summary>
[Collection("CDK Tests")]
public class PrivateStaticAssetsGoldenTemplateTests
{
    [Fact]
    public void PrivateStaticAssets_Defaults_ShouldMatchGoldenTemplate()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack("golden-private-static-assets");

        _ = new PrivateStaticAssetsConstruct(stack, "assets", new PrivateStaticAssetsConstructProps
        {
            AssetPath = CdkTestHelper.GetTestAssetPath("TestAssets/static-site"),
        });

        GoldenTemplate.AssertMatches(stack, "private-static-assets-default.json");
    }

    [Fact]
    public void PrivateStaticAssets_CustomDomain_ShouldMatchGoldenTemplate()
    {
        var (_, stack) = CdkTestHelper.CreateTestStack("golden-private-static-assets");

        var zone = HostedZone.FromHostedZoneAttributes(stack, "HostedZone", new HostedZoneAttributes
        {
            HostedZoneId = "Z1234567890ABC",
            ZoneName = "example.com",
        });

        var certificate = Certificate.FromCertificateArn(
            stack,
            "Certificate",
            "arn:aws:acm:us-east-1:123456789012:certificate/11111111-2222-3333-4444-555555555555");

        _ = new PrivateStaticAssetsConstruct(stack, "assets", new PrivateStaticAssetsConstructProps
        {
            AssetPath = CdkTestHelper.GetTestAssetPath("TestAssets/static-site"),
            DestinationKeyPrefix = "game/abc123/",
            CacheControl = [CacheControl.SetPublic(), CacheControl.MaxAge(Duration.Days(365)), CacheControl.Immutable()],
            Domain = new PrivateStaticAssetsDomainOptions
            {
                DomainNames = ["assets.example.com", "cdn.example.com"],
                Certificate = certificate,
                HostedZone = zone,
            },
        });

        GoldenTemplate.AssertMatches(stack, "private-static-assets-custom-domain.json");
    }
}

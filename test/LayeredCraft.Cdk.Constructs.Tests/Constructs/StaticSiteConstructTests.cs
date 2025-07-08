using Amazon.CDK.Assertions;
using LayeredCraft.Cdk.Constructs.Constructs;
using LayeredCraft.Cdk.Constructs.Models;
using LayeredCraft.Cdk.Constructs.Testing;
using LayeredCraft.Cdk.Constructs.Tests.TestKit.Attributes;

namespace LayeredCraft.Cdk.Constructs.Tests.Constructs;

[Collection("CDK Tests")]
public class StaticSiteConstructTests
{
    [Fact]
    public void Construct_ShouldCreateBasicStaticSite()
    {
        // Arrange
        var (_, stack) = CdkTestHelper.CreateTestStack();
        var props = CdkTestHelper.CreateStaticSitePropsBuilder()
            .WithDomainName("example.com")
            .WithSiteSubDomain("www")
            .WithAssetPath(CdkTestHelper.GetTestAssetPath("TestAssets/static-site"))
            .WithoutApiDomain()
            .WithoutAlternateDomains()
            .Build();

        // Act
        _ = new StaticSiteConstruct(stack, "TestStaticSite", props);
        var template = Template.FromStack(stack);

        // Assert
        template.ShouldHaveCompleteStaticSite("www.example.com", "www.example.com", hasApiProxy: false, domainCount: 1);
    }

    [Theory]
    [StaticSiteConstructAutoData(includeApiDomain: false, includeAlternateDomains: false)]
    public void Construct_WithAutoFixtureData_ShouldCreateBasicStaticSite(StaticSiteConstructProps props)
    {
        // Arrange
        var (_, stack) = CdkTestHelper.CreateTestStack();

        // Act
        _ = new StaticSiteConstruct(stack, "TestStaticSite", props);
        var template = Template.FromStack(stack);

        // Assert
        var expectedDomain = $"{props.SiteSubDomain}.{props.DomainName}";
        template.ShouldHaveStaticWebsiteBucket(expectedDomain);
        template.ShouldHaveCloudFrontDistribution(expectedDomain);
        template.ShouldHaveSSLCertificate(expectedDomain);
        template.ShouldHaveRoute53Records(expectedDomain);
        template.ShouldNotHaveApiProxyBehavior();
    }

    [Theory]
    [StaticSiteConstructAutoData(includeApiDomain: true, includeAlternateDomains: false)]
    public void Construct_WithApiDomain_ShouldCreateApiProxyBehavior(StaticSiteConstructProps props)
    {
        // Arrange
        var (_, stack) = CdkTestHelper.CreateTestStack();

        // Act
        _ = new StaticSiteConstruct(stack, "TestStaticSite", props);
        var template = Template.FromStack(stack);

        // Assert
        var expectedDomain = $"{props.SiteSubDomain}.{props.DomainName}";
        template.ShouldHaveCompleteStaticSite(expectedDomain, expectedDomain, hasApiProxy: true, domainCount: 1);
        template.ShouldHaveApiProxyBehavior(props.ApiDomain!);
    }

    [Theory]
    [StaticSiteConstructAutoData(includeApiDomain: false, includeAlternateDomains: true)]
    public void Construct_WithAlternateDomains_ShouldCreateMultipleRoute53Records(StaticSiteConstructProps props)
    {
        // Arrange
        var (_, stack) = CdkTestHelper.CreateTestStack();

        // Act
        _ = new StaticSiteConstruct(stack, "TestStaticSite", props);
        var template = Template.FromStack(stack);

        // Assert
        var expectedDomain = $"{props.SiteSubDomain}.{props.DomainName}";
        var expectedRecordCount = 1 + props.AlternateDomains.Length; // Primary + alternates
        template.ShouldHaveCompleteStaticSite(expectedDomain, expectedDomain, hasApiProxy: false, domainCount: expectedRecordCount);
    }

    [Fact]
    public void Construct_ForBlogScenario_ShouldCreateProperConfiguration()
    {
        // Arrange
        var (_, stack) = CdkTestHelper.CreateTestStack();
        var props = CdkTestHelper.CreateStaticSitePropsBuilder()
            .ForBlog("myblog.com")
            .WithAssetPath(CdkTestHelper.GetTestAssetPath("TestAssets/static-site"))
            .Build();

        // Act
        _ = new StaticSiteConstruct(stack, "BlogSite", props);
        var template = Template.FromStack(stack);

        // Assert
        template.ShouldHaveCompleteStaticSite("www.myblog.com", "www.myblog.com", hasApiProxy: false, domainCount: 2);
        template.ShouldHaveRoute53Records("www.myblog.com");
        template.ShouldHaveRoute53Records("myblog.com");
    }

    [Fact]
    public void Construct_ForDocumentationScenario_ShouldCreateDocsSubdomain()
    {
        // Arrange
        var (_, stack) = CdkTestHelper.CreateTestStack();
        var props = CdkTestHelper.CreateStaticSitePropsBuilder()
            .ForDocumentation("mycompany.com", "api.mycompany.com")
            .WithAssetPath(CdkTestHelper.GetTestAssetPath("TestAssets/static-site"))
            .Build();

        // Act
        _ = new StaticSiteConstruct(stack, "DocsSite", props);
        var template = Template.FromStack(stack);

        // Assert
        template.ShouldHaveCompleteStaticSite("docs.mycompany.com", "docs.mycompany.com", hasApiProxy: true, domainCount: 1);
        template.ShouldHaveApiProxyBehavior("api.mycompany.com");
    }

    [Fact]
    public void Construct_ForSinglePageAppScenario_ShouldCreateAppSubdomainWithApi()
    {
        // Arrange
        var (_, stack) = CdkTestHelper.CreateTestStack();
        var props = CdkTestHelper.CreateStaticSitePropsBuilder()
            .ForSinglePageApp("myapp.com", "api.myapp.com")
            .WithAssetPath(CdkTestHelper.GetTestAssetPath("TestAssets/static-site"))
            .Build();

        // Act
        _ = new StaticSiteConstruct(stack, "AppSite", props);
        var template = Template.FromStack(stack);

        // Assert
        template.ShouldHaveCompleteStaticSite("app.myapp.com", "app.myapp.com", hasApiProxy: true, domainCount: 1);
        template.ShouldHaveApiProxyBehavior("api.myapp.com");
    }

    [Fact]
    public void Construct_ShouldCreateS3BucketWithCorrectConfiguration()
    {
        // Arrange
        var (_, stack) = CdkTestHelper.CreateTestStack();
        var props = CdkTestHelper.CreateStaticSitePropsBuilder()
            .WithDomainName("test.com")
            .WithSiteSubDomain("www")
            .WithAssetPath(CdkTestHelper.GetTestAssetPath("TestAssets/static-site"))
            .Build();

        // Act
        _ = new StaticSiteConstruct(stack, "TestSite", props);
        var template = Template.FromStack(stack);

        // Assert
        template.HasResourceProperties("AWS::S3::Bucket", Match.ObjectLike(new Dictionary<string, object>
        {
            { "BucketName", "www.test.com" },
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

    [Fact]
    public void Construct_ShouldCreateCloudFrontDistributionWithCorrectCaching()
    {
        // Arrange
        var (_, stack) = CdkTestHelper.CreateTestStack();
        var props = CdkTestHelper.CreateStaticSitePropsBuilder()
            .WithDomainName("test.com")
            .WithSiteSubDomain("www")
            .WithAssetPath(CdkTestHelper.GetTestAssetPath("TestAssets/static-site"))
            .Build();

        // Act
        _ = new StaticSiteConstruct(stack, "TestSite", props);
        var template = Template.FromStack(stack);

        // Assert
        template.HasResourceProperties("AWS::CloudFront::Distribution", Match.ObjectLike(new Dictionary<string, object>
        {
            { "DistributionConfig", Match.ObjectLike(new Dictionary<string, object>
            {
                { "Enabled", true },
                { "Aliases", Match.ArrayWith(["www.test.com"]) },
                { "DefaultCacheBehavior", Match.ObjectLike(new Dictionary<string, object>
                {
                    { "AllowedMethods", Match.ArrayWith(["GET", "HEAD"]) },
                    { "Compress", true }
                }) },
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

    [Fact]
    public void Construct_ShouldCreateBucketDeploymentWithCorrectConfiguration()
    {
        // Arrange
        var (_, stack) = CdkTestHelper.CreateTestStack();
        var props = CdkTestHelper.CreateStaticSitePropsBuilder()
            .WithAssetPath(CdkTestHelper.GetTestAssetPath("TestAssets/static-site"))
            .Build();

        // Act
        _ = new StaticSiteConstruct(stack, "TestSite", props);
        var template = Template.FromStack(stack);

        // Assert
        template.ShouldHaveBucketDeployment();
        template.HasResourceProperties("Custom::CDKBucketDeployment", Match.ObjectLike(new Dictionary<string, object>
        {
            { "DistributionPaths", Match.ArrayWith(["/*"]) }
        }));
    }

    [Fact]
    public void Construct_WithNullApiDomain_ShouldNotCreateApiProxyBehavior()
    {
        // Arrange
        var (_, stack) = CdkTestHelper.CreateTestStack();
        var props = CdkTestHelper.CreateStaticSitePropsBuilder()
            .WithoutApiDomain()
            .Build();

        // Act
        _ = new StaticSiteConstruct(stack, "TestSite", props);
        var template = Template.FromStack(stack);

        // Assert
        template.ShouldNotHaveApiProxyBehavior();
    }

    [Fact]
    public void Construct_ShouldCreateMinimalResources()
    {
        // Arrange
        var (_, stack) = CdkTestHelper.CreateTestStack();
        var props = CdkTestHelper.CreateStaticSitePropsBuilder()
            .WithoutApiDomain()
            .Build();

        // Act
        _ = new StaticSiteConstruct(stack, "TestSite", props);
        var template = Template.FromStack(stack);

        // Assert - Test only basic resource creation without BucketDeployment
        template.ResourceCountIs("AWS::S3::Bucket", 1);
        template.ResourceCountIs("AWS::CloudFront::Distribution", 1);
        template.ResourceCountIs("AWS::CertificateManager::Certificate", 1);
        template.ResourceCountIs("AWS::Route53::RecordSet", 1);
    }

    [Fact]
    public void Construct_WithEmptyApiDomain_ShouldNotCreateApiProxyBehavior()
    {
        // Arrange
        var (_, stack) = CdkTestHelper.CreateTestStack();
        var props = CdkTestHelper.CreateStaticSitePropsBuilder()
            .WithApiDomain("")
            .Build();

        // Act
        _ = new StaticSiteConstruct(stack, "TestSite", props);
        var template = Template.FromStack(stack);

        // Assert
        template.ShouldNotHaveApiProxyBehavior();
    }

    [Fact]
    public void Construct_WithWhitespaceApiDomain_ShouldNotCreateApiProxyBehavior()
    {
        // Arrange
        var (_, stack) = CdkTestHelper.CreateTestStack();
        var props = CdkTestHelper.CreateStaticSitePropsBuilder()
            .WithApiDomain("   ")
            .Build();

        // Act
        _ = new StaticSiteConstruct(stack, "TestSite", props);
        var template = Template.FromStack(stack);

        // Assert
        template.ShouldNotHaveApiProxyBehavior();
    }

    [Fact]
    public void Construct_WithSingleAlternateDomain_ShouldCreateTwoRoute53Records()
    {
        // Arrange
        var (_, stack) = CdkTestHelper.CreateTestStack();
        var props = CdkTestHelper.CreateStaticSitePropsBuilder()
            .WithAlternateDomain("alt.example.com")
            .WithAssetPath(CdkTestHelper.GetTestAssetPath("TestAssets/static-site"))
            .Build();

        // Act
        _ = new StaticSiteConstruct(stack, "TestSite", props);
        var template = Template.FromStack(stack);

        // Assert
        template.ShouldHaveCompleteStaticSite("www.example.com", "www.example.com", hasApiProxy: false, domainCount: 2);
        template.ShouldHaveRoute53Records("www.example.com");
        template.ShouldHaveRoute53Records("alt.example.com");
    }

    [Fact]
    public void Construct_WithMultipleAlternateDomains_ShouldCreateCorrectNumberOfRoute53Records()
    {
        // Arrange
        var (_, stack) = CdkTestHelper.CreateTestStack();
        var props = CdkTestHelper.CreateStaticSitePropsBuilder()
            .WithAlternateDomains("alt1.example.com", "alt2.example.com", "alt3.example.com")
            .WithAssetPath(CdkTestHelper.GetTestAssetPath("TestAssets/static-site"))
            .Build();

        // Act
        _ = new StaticSiteConstruct(stack, "TestSite", props);
        var template = Template.FromStack(stack);

        // Assert
        template.ShouldHaveCompleteStaticSite("www.example.com", "www.example.com", hasApiProxy: false, domainCount: 4);
        template.ShouldHaveRoute53Records("www.example.com");
        template.ShouldHaveRoute53Records("alt1.example.com");
        template.ShouldHaveRoute53Records("alt2.example.com");
        template.ShouldHaveRoute53Records("alt3.example.com");
    }

    [Fact]
    public void Construct_WithoutAlternateDomains_ShouldClearExistingAlternates()
    {
        // Arrange
        var (_, stack) = CdkTestHelper.CreateTestStack();
        var props = CdkTestHelper.CreateStaticSitePropsBuilder()
            .WithAlternateDomains("temp1.example.com", "temp2.example.com")
            .WithoutAlternateDomains()
            .WithAssetPath(CdkTestHelper.GetTestAssetPath("TestAssets/static-site"))
            .Build();

        // Act
        _ = new StaticSiteConstruct(stack, "TestSite", props);
        var template = Template.FromStack(stack);

        // Assert
        template.ShouldHaveCompleteStaticSite("www.example.com", "www.example.com", hasApiProxy: false, domainCount: 1);
        template.ShouldHaveRoute53Records("www.example.com");
    }
}
using LayeredCraft.Cdk.Constructs.Models;
using LayeredCraft.Cdk.Constructs.Testing;

namespace LayeredCraft.Cdk.Constructs.Tests.TestKit.Customizations;

/// <summary>
/// AutoFixture customization for StaticSiteConstructProps to provide realistic test data.
/// </summary>
public class StaticSiteConstructCustomization(bool includeApiDomain = true, bool includeAlternateDomains = true)
    : ICustomization
{
    public void Customize(IFixture fixture)
    {
        fixture.Customize<StaticSiteConstructProps>(transform => transform
            .With(props => props.DomainName, "example.com")
            .With(props => props.SiteSubDomain, "www")
            .With(props => props.AssetPath, CdkTestHelper.GetTestAssetPath("TestAssets/static-site"))
            .With(props => props.ApiDomain, includeApiDomain ? "api.example.com" : null)
            .With(props => props.AlternateDomains, includeAlternateDomains 
                ? ["example.com", "alt.example.com"] 
                : []));
    }
}
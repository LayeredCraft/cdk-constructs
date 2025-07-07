using AutoFixture.Xunit3;
using LayeredCraft.Cdk.Constructs.Tests.TestKit.Customizations;

namespace LayeredCraft.Cdk.Constructs.Tests.TestKit.Attributes;

/// <summary>
/// AutoData attribute for StaticSiteConstruct tests with customizable configuration.
/// </summary>
public class StaticSiteConstructAutoDataAttribute(bool includeApiDomain = true, bool includeAlternateDomains = true) : AutoDataAttribute(() => CreateFixture(includeApiDomain, includeAlternateDomains))
{
    private static IFixture CreateFixture(bool includeApiDomain, bool includeAlternateDomains)
    {
        return BaseFixtureFactory.CreateFixture(fixture =>
        {
            fixture.Customize(new StaticSiteConstructCustomization(includeApiDomain, includeAlternateDomains));
        });
    }
}
using AutoFixture.Xunit3;
using LayeredCraft.Cdk.Constructs.Tests.TestKit.Customizations;

namespace LayeredCraft.Cdk.Constructs.Tests.TestKit.Attributes;

public class LambdaFunctionConstructAutoDataAttribute(bool includeOtelLayer = false, bool includePermissions = true, bool generateUrl = false) : AutoDataAttribute(() => CreateFixture(includeOtelLayer, includePermissions, generateUrl))
{
    private static IFixture CreateFixture(bool includeOtelLayer, bool includePermissions, bool generateUrl)
    {
        return BaseFixtureFactory.CreateFixture(fixture =>
        {
            fixture.Customize(new LambdaFunctionConstructCustomization(includeOtelLayer, includePermissions, generateUrl));
        });
    }
}
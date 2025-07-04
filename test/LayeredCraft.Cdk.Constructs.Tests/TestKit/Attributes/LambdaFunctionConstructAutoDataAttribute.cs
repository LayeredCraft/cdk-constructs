using AutoFixture.Xunit3;
using LayeredCraft.Cdk.Constructs.Tests.TestKit.Customizations;

namespace LayeredCraft.Cdk.Constructs.Tests.TestKit.Attributes;

public class LambdaFunctionConstructAutoDataAttribute(bool includeOtelLayer = true, bool includePermissions = true) : AutoDataAttribute(() => CreateFixture(includeOtelLayer, includePermissions))
{
    private static IFixture CreateFixture(bool includeOtelLayer, bool includePermissions)
    {
        return BaseFixtureFactory.CreateFixture(fixture =>
        {
            fixture.Customize(new LambdaFunctionConstructCustomization(includeOtelLayer, includePermissions));
        });
    }
}
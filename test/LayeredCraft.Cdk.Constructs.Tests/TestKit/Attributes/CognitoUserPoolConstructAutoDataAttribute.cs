using AutoFixture.Xunit3;
using LayeredCraft.Cdk.Constructs.Tests.TestKit.Customizations;

namespace LayeredCraft.Cdk.Constructs.Tests.TestKit.Attributes;

public class CognitoUserPoolConstructAutoDataAttribute(bool includeBranding = false)
    : AutoDataAttribute(() => CreateFixture(includeBranding))
{
    private static IFixture CreateFixture(bool includeBranding)
    {
        return BaseFixtureFactory.CreateFixture(fixture =>
        {
            fixture.Customize(new CognitoUserPoolConstructCustomization(includeBranding));
        });
    }
}

using AutoFixture.Xunit3;
using LayeredCraft.Cdk.Constructs.Tests.TestKit.Customizations;

namespace LayeredCraft.Cdk.Constructs.Tests.TestKit.Attributes;

public class DynamoDbTableConstructAutoDataAttribute(bool includeGsi = false, bool includeStream = false, bool includeTtl = false) : AutoDataAttribute(() => CreateFixture(includeGsi, includeStream, includeTtl))
{
    private static IFixture CreateFixture(bool includeGsi, bool includeStream, bool includeTtl)
    {
        return BaseFixtureFactory.CreateFixture(fixture =>
        {
            fixture.Customize(new DynamoDbTableConstructCustomization(includeGsi, includeStream, includeTtl));
        });
    }
}
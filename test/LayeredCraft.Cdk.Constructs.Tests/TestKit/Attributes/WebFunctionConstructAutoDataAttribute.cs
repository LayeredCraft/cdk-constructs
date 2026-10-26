using AutoFixture.Xunit3;
using LayeredCraft.Cdk.Constructs.Tests.TestKit.Customizations;

namespace LayeredCraft.Cdk.Constructs.Tests.TestKit.Attributes;

/// <summary>
/// AutoData attribute that supplies <see cref="LayeredCraft.Cdk.Constructs.Models.WebFunctionConstructProps"/>
/// with deterministic test values. The default code artifact is an imported, externally managed bucket so
/// tests synthesize without AWS credentials or a bucket resource.
/// </summary>
public class WebFunctionConstructAutoDataAttribute() : AutoDataAttribute(
    () => BaseFixtureFactory.CreateFixture(fixture =>
    {
        fixture.Customize(new WebFunctionConstructCustomization());
    }));

using Compono;
using LayeredCraft.Cdk.Constructs.Models;
using LayeredCraft.Cdk.Constructs.Testing;

namespace LayeredCraft.Cdk.Constructs.Tests.TestKit.Profiles;

/// <summary>
/// Supplies deterministic <see cref="PrivateStaticAssetsConstructProps"/> test data. The required
/// <see cref="PrivateStaticAssetsConstructProps.AssetPath"/> is a real test asset directory; every other member
/// keeps the construct's documented default so tests can assert that default explicitly.
/// </summary>
public sealed class PrivateStaticAssetsConstructPropsProfile : ICompositionProfile
{
    // Compono does not provide this because the required AssetPath must point at a real, existing test asset
    // directory (composition would generate an arbitrary string that fails at synthesis), so a registration
    // factory supplies the whole value.
    public void Configure(CompositionBuilder builder) =>
        builder.Register(() => new PrivateStaticAssetsConstructProps
        {
            AssetPath = CdkTestHelper.GetTestAssetPath("TestAssets/static-site"),
        });
}

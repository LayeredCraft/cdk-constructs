using System.Reflection;

namespace LayeredCraft.Cdk.Constructs.Tests.TestKit.Extensions;

/// <summary>
/// Extension methods for resolving test asset paths relative to the executing assembly.
/// </summary>
public static class AssetPathExtensions
{
    /// <summary>
    /// Gets the path to a test asset relative to the executing assembly location.
    /// This ensures test assets can be found regardless of the current working directory.
    /// </summary>
    /// <param name="relativePath">Relative path from the test assembly location (e.g., "TestAssets/test-lambda.zip")</param>
    /// <returns>Absolute path to the test asset</returns>
    public static string GetTestAssetPath(string relativePath)
    {
        var assemblyLocation = Assembly.GetExecutingAssembly().Location;
        var assemblyDirectory = Path.GetDirectoryName(assemblyLocation) ?? throw new InvalidOperationException("Could not determine assembly directory");
        return Path.Combine(assemblyDirectory, relativePath);
    }

    /// <summary>
    /// Gets the standard test Lambda deployment package path.
    /// This is a convenience method for the most commonly used test asset.
    /// </summary>
    /// <returns>Absolute path to the test Lambda zip file</returns>
    public static string GetTestLambdaZipPath()
    {
        return GetTestAssetPath("TestAssets/test-lambda.zip");
    }
}
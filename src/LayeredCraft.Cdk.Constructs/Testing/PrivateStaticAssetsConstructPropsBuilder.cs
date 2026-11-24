using Amazon.CDK;
using Amazon.CDK.AWS.CloudFront;
using Amazon.CDK.AWS.S3.Deployment;
using LayeredCraft.Cdk.Constructs.Models;

namespace LayeredCraft.Cdk.Constructs.Testing;

/// <summary>
/// Fluent builder for <see cref="PrivateStaticAssetsConstructProps"/> used by tests. It mirrors the
/// repository's builder convention (see <c>StaticSiteConstructPropsBuilder</c>,
/// <c>WebFunctionConstructPropsBuilder</c>): private fields, <c>With*</c> methods that return <c>this</c>, and a
/// <see cref="Build"/> method that returns the record.
/// </summary>
public class PrivateStaticAssetsConstructPropsBuilder
{
    private string _assetPath = CdkTestHelper.GetTestAssetPath("TestAssets/static-site");
    private string? _destinationKeyPrefix;
    private bool _prune;
    private bool _retainOnDelete = true;
    private readonly List<CacheControl> _cacheControl = new();
    private string[]? _invalidationPaths;
    private RemovalPolicy _removalPolicy = RemovalPolicy.RETAIN;
    private ViewerProtocolPolicy _viewerProtocolPolicy = ViewerProtocolPolicy.HTTPS_ONLY;
    private ICachePolicy _cachePolicy = CachePolicy.CACHING_OPTIMIZED;
    private string? _comment;
    private bool _spaRouting;
    private readonly List<IErrorResponse> _errorResponses = new();
    private string? _defaultRootObject;
    private PrivateStaticAssetsDomainOptions? _domain;

    /// <summary>Sets the asset directory deployed to the bucket.</summary>
    public PrivateStaticAssetsConstructPropsBuilder WithAssetPath(string assetPath)
    {
        _assetPath = assetPath;
        return this;
    }

    /// <summary>Sets the destination key prefix in the bucket.</summary>
    public PrivateStaticAssetsConstructPropsBuilder WithDestinationKeyPrefix(string destinationKeyPrefix)
    {
        _destinationKeyPrefix = destinationKeyPrefix;
        return this;
    }

    /// <summary>Sets whether the deployment prunes objects that are not in the asset (default: <c>true</c>).</summary>
    public PrivateStaticAssetsConstructPropsBuilder WithPrune(bool prune = true)
    {
        _prune = prune;
        return this;
    }

    /// <summary>Sets whether deployed objects are retained when the deployment resource is removed.</summary>
    public PrivateStaticAssetsConstructPropsBuilder WithRetainOnDelete(bool retainOnDelete)
    {
        _retainOnDelete = retainOnDelete;
        return this;
    }

    /// <summary>Adds <c>Cache-Control</c> metadata applied to every deployed object.</summary>
    public PrivateStaticAssetsConstructPropsBuilder WithCacheControl(params CacheControl[] cacheControl)
    {
        _cacheControl.AddRange(cacheControl);
        return this;
    }

    /// <summary>Sets the CloudFront invalidation paths applied after deployment.</summary>
    public PrivateStaticAssetsConstructPropsBuilder WithInvalidationPaths(params string[] invalidationPaths)
    {
        _invalidationPaths = invalidationPaths;
        return this;
    }

    /// <summary>Sets the bucket removal policy (default: <see cref="RemovalPolicy.RETAIN"/>).</summary>
    public PrivateStaticAssetsConstructPropsBuilder WithRemovalPolicy(RemovalPolicy removalPolicy)
    {
        _removalPolicy = removalPolicy;
        return this;
    }

    /// <summary>Sets the default behavior's viewer protocol policy (default: HTTPS-only).</summary>
    public PrivateStaticAssetsConstructPropsBuilder WithViewerProtocolPolicy(ViewerProtocolPolicy viewerProtocolPolicy)
    {
        _viewerProtocolPolicy = viewerProtocolPolicy;
        return this;
    }

    /// <summary>Sets the default behavior's cache policy (default: <see cref="CachePolicy.CACHING_OPTIMIZED"/>).</summary>
    public PrivateStaticAssetsConstructPropsBuilder WithCachePolicy(ICachePolicy cachePolicy)
    {
        _cachePolicy = cachePolicy;
        return this;
    }

    /// <summary>Sets the CloudFront distribution comment.</summary>
    public PrivateStaticAssetsConstructPropsBuilder WithComment(string comment)
    {
        _comment = comment;
        return this;
    }

    /// <summary>Sets whether SPA route rewriting is enabled (default: <c>true</c>).</summary>
    public PrivateStaticAssetsConstructPropsBuilder WithSpaRouting(bool spaRouting = true)
    {
        _spaRouting = spaRouting;
        return this;
    }

    /// <summary>Adds a custom error response to the distribution.</summary>
    public PrivateStaticAssetsConstructPropsBuilder WithErrorResponse(IErrorResponse errorResponse)
    {
        _errorResponses.Add(errorResponse);
        return this;
    }

    /// <summary>Sets the distribution's default root object.</summary>
    public PrivateStaticAssetsConstructPropsBuilder WithDefaultRootObject(string defaultRootObject)
    {
        _defaultRootObject = defaultRootObject;
        return this;
    }

    /// <summary>Sets the optional custom DNS configuration.</summary>
    public PrivateStaticAssetsConstructPropsBuilder WithDomain(PrivateStaticAssetsDomainOptions domain)
    {
        _domain = domain;
        return this;
    }

    /// <summary>Builds the <see cref="PrivateStaticAssetsConstructProps"/>.</summary>
    public PrivateStaticAssetsConstructProps Build() => new()
    {
        AssetPath = _assetPath,
        DestinationKeyPrefix = _destinationKeyPrefix,
        Prune = _prune,
        RetainOnDelete = _retainOnDelete,
        CacheControl = _cacheControl.ToArray(),
        InvalidationPaths = _invalidationPaths,
        RemovalPolicy = _removalPolicy,
        ViewerProtocolPolicy = _viewerProtocolPolicy,
        CachePolicy = _cachePolicy,
        Comment = _comment,
        SpaRouting = _spaRouting,
        ErrorResponses = _errorResponses.ToArray(),
        DefaultRootObject = _defaultRootObject,
        Domain = _domain,
    };
}

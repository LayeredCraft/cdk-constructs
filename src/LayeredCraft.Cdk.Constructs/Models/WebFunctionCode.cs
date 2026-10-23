using System;
using Amazon.CDK.AWS.S3;

namespace LayeredCraft.Cdk.Constructs.Models;

/// <summary>
/// The S3 artifact a Web Function revision runs from. The hierarchy is closed; additional sources (for
/// example an asset) can be added in a later release without being a breaking change.
/// </summary>
public abstract class WebFunctionCode
{
    private protected WebFunctionCode()
    {
    }

    /// <summary>
    /// Runs the revision from a zip object in an S3 bucket.
    /// </summary>
    /// <param name="bucket">The bucket that holds the artifact.</param>
    /// <param name="key">The object key of the artifact zip.</param>
    /// <param name="options">Optional version pinning and bucket-policy options.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="bucket"/> is null.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="key"/> is null or empty.</exception>
    public static WebFunctionCode FromBucket(IBucket bucket, string key, WebFunctionCodeFromBucketOptions? options = null)
    {
        if (bucket is null)
            throw new ArgumentNullException(nameof(bucket));
        if (string.IsNullOrEmpty(key))
            throw new ArgumentException("Artifact key must be a non-empty value.", nameof(key));

        return new WebFunctionCodeFromBucket(bucket, key, options ?? new WebFunctionCodeFromBucketOptions());
    }
}

/// <summary>
/// Code sourced from a single object in an S3 bucket.
/// </summary>
internal sealed class WebFunctionCodeFromBucket : WebFunctionCode
{
    internal WebFunctionCodeFromBucket(IBucket bucket, string key, WebFunctionCodeFromBucketOptions options)
    {
        Bucket = bucket;
        Key = key;
        Options = options;
    }

    internal IBucket Bucket { get; }

    internal string Key { get; }

    internal WebFunctionCodeFromBucketOptions Options { get; }
}

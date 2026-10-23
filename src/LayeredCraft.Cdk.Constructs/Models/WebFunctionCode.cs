using System;
using System.Collections.Generic;
using System.Linq;
using Amazon.CDK;
using Amazon.CDK.AWS.IAM;
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

    /// <summary>
    /// Resolves the S3 object and, for automatic bucket policies, grants the Web Function read access to the
    /// single artifact object.
    /// </summary>
    /// <param name="function">The Web Function that will run this artifact.</param>
    internal abstract WebFunctionCodeConfig Bind(WebFunctionConstruct function);
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

    internal override WebFunctionCodeConfig Bind(WebFunctionConstruct function)
    {
        ArgumentNullException.ThrowIfNull(function);

        ValidateOwnedBucketConfiguration();

        if (Options.BucketPolicy == WebFunctionCodeBucketPolicy.ExternallyManaged)
            return new WebFunctionCodeConfig(Bucket.BucketName, Key, Options.ObjectVersion, null);

        var functionStack = Stack.Of(function);
        if (Amazon.CDK.Resource.IsOwnedResource(Bucket)
            && !ReferenceEquals(Stack.Of(Bucket), functionStack)
            && (Token.IsUnresolved(functionStack.Account) || Token.IsUnresolved(functionStack.Region)))
        {
            throw new InvalidOperationException(
                "The artifact bucket belongs to a different stack than the Web Function. Set an explicit account " +
                "and Region on the Web Function stack so the bucket policy can be rendered.");
        }

        var partition = functionStack.Partition;
        var account = functionStack.Account;
        var region = functionStack.Region;

        var statement = new PolicyStatement(new PolicyStatementProps
        {
            Effect = Effect.ALLOW,
            Actions = ["s3:GetObject", "s3:GetObjectVersion"],
            Resources = [$"arn:{partition}:s3:::{Bucket.BucketName}/{Key}"],
            Principals = [new ServicePrincipal("lambda.amazonaws.com")],
            Conditions = new Dictionary<string, object>
            {
                ["StringEquals"] = new Dictionary<string, string>
                {
                    ["aws:SourceAccount"] = account,
                },
                ["ArnLike"] = new Dictionary<string, string>
                {
                    ["aws:SourceArn"] = $"arn:{partition}:lambda:{region}:{account}:web-function/" +
                                        $"{function.FunctionName}/revision/*",
                },
            },
        });

        var result = Bucket.AddToResourcePolicy(statement);
        if (!result.StatementAdded)
        {
            throw new InvalidOperationException(
                "The artifact bucket did not accept the required read policy. Use " +
                "WebFunctionCodeBucketPolicy.ExternallyManaged and grant lambda.amazonaws.com read access to the " +
                "artifact yourself.");
        }

        return new WebFunctionCodeConfig(Bucket.BucketName, Key, Options.ObjectVersion, result.PolicyDependable);
    }

    private void ValidateOwnedBucketConfiguration()
    {
        if (!Amazon.CDK.Resource.IsOwnedResource(Bucket))
            return;

        if (Bucket.Node.DefaultChild is not CfnBucket cfnBucket)
            return;

        // The bucket L2 sets these properties from JavaScript, so the CLR getters expose a JSII anonymous object.
        // Resolving through the stack gives the plain object that was actually configured.
        var stack = Stack.Of(Bucket);

        var versioningRaw = cfnBucket.VersioningConfiguration;
        if (versioningRaw is null)
            throw new InvalidOperationException(VersioningMessage);

        var versioning = stack.Resolve(versioningRaw);
        if (versioning is IDictionary<string, object> versioningMap
            && !string.Equals(GetString(versioningMap, "status"), "Enabled", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(VersioningMessage);
        }

        var encryptionRaw = cfnBucket.BucketEncryption;
        if (encryptionRaw is null)
            return;

        var encryption = stack.Resolve(encryptionRaw);
        if (encryption is not IDictionary<string, object> encryptionMap
            || !TryGetValue(encryptionMap, "serverSideEncryptionConfiguration", out var rulesRaw)
            || rulesRaw is not IEnumerable<object?> rules
            || rules.FirstOrDefault() is not IDictionary<string, object> rule
            || !TryGetValue(rule, "serverSideEncryptionByDefault", out var byDefaultRaw)
            || byDefaultRaw is not IDictionary<string, object> byDefault)
        {
            return;
        }

        if (UsesAwsManagedKey(GetString(byDefault, "sseAlgorithm"), GetString(byDefault, "kmsMasterKeyId")))
        {
            throw new InvalidOperationException(
                "An owned artifact bucket cannot use an AWS-managed KMS key. Use S3-managed encryption or a " +
                "customer-managed key the function role can read.");
        }
    }

    private const string VersioningMessage =
        "An owned artifact bucket must have versioning enabled so a revision keeps the artifact version it was " +
        "created with.";

    private static bool TryGetValue(IDictionary<string, object> map, string key, out object? value)
    {
        foreach (var entry in map)
        {
            if (string.Equals(entry.Key, key, StringComparison.OrdinalIgnoreCase))
            {
                value = entry.Value;
                return true;
            }
        }

        value = null;
        return false;
    }

    private static string? GetString(IDictionary<string, object> map, string key)
        => TryGetValue(map, key, out var value) ? value?.ToString() : null;

    private static bool UsesAwsManagedKey(string? algorithm, string? kmsKeyId)
    {
        if (string.Equals(algorithm, "aws:kms:dsse", StringComparison.OrdinalIgnoreCase))
            return true;

        if (!string.Equals(algorithm, "aws:kms", StringComparison.OrdinalIgnoreCase))
            return false;

        return string.IsNullOrEmpty(kmsKeyId)
               || string.Equals(kmsKeyId, "aws/s3", StringComparison.OrdinalIgnoreCase);
    }
}

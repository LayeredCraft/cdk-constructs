namespace LayeredCraft.Cdk.Constructs.Models;

/// <summary>
/// Who maintains the <c>lambda.amazonaws.com</c> read statement on the code bucket.
/// </summary>
public enum WebFunctionCodeBucketPolicy
{
    /// <summary>
    /// The construct adds a least-privilege statement. Requires a bucket the app can modify (an owned
    /// <c>IBucket</c>); an imported bucket fails synthesis.
    /// </summary>
    Automatic,

    /// <summary>
    /// The bucket policy is managed elsewhere (another stack, account, or process). Nothing is added.
    /// </summary>
    ExternallyManaged,
}

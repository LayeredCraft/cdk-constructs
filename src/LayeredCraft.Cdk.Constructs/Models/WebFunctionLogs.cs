using System;
using Amazon.CDK;
using Amazon.CDK.AWS.Logs;

namespace LayeredCraft.Cdk.Constructs.Models;

/// <summary>
/// Where a Web Function revision's logs go. A closed choice, so "supplied group" and "create a group" can
/// never be combined or contradict each other.
/// </summary>
public abstract class WebFunctionLogs
{
    private protected WebFunctionLogs()
    {
    }

    /// <summary>
    /// Creates a log group named <c>/aws/lambda/web/{FunctionName}</c>. Defaults to
    /// <see cref="RetentionDays.TWO_WEEKS"/> retention and <see cref="RemovalPolicy.DESTROY"/>.
    /// </summary>
    /// <param name="retention">The retention for the created group. Defaults to two weeks.</param>
    /// <param name="removalPolicy">The removal policy for the created group. Defaults to destroy.</param>
    public static WebFunctionLogs Create(RetentionDays? retention = null, RemovalPolicy? removalPolicy = null)
        => new WebFunctionLogsCreate(retention ?? RetentionDays.TWO_WEEKS, removalPolicy ?? RemovalPolicy.DESTROY);

    /// <summary>Writes to an existing log group, passed by name to the revision.</summary>
    /// <param name="logGroup">The existing log group.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="logGroup"/> is null.</exception>
    public static WebFunctionLogs Use(ILogGroup logGroup)
        => new WebFunctionLogsUse(logGroup ?? throw new ArgumentNullException(nameof(logGroup)));

    /// <summary>
    /// Creates nothing and lets Lambda use its default group (<c>/aws/lambda/web/{FunctionName}</c>), which
    /// has no expiry.
    /// </summary>
    public static WebFunctionLogs ServiceDefault() => new WebFunctionLogsServiceDefault();
}

/// <summary>Creates a managed log group for the revision.</summary>
internal sealed class WebFunctionLogsCreate : WebFunctionLogs
{
    internal WebFunctionLogsCreate(RetentionDays retention, RemovalPolicy removalPolicy)
    {
        Retention = retention;
        RemovalPolicy = removalPolicy;
    }

    internal RetentionDays Retention { get; }

    internal RemovalPolicy RemovalPolicy { get; }
}

/// <summary>Writes to an existing log group.</summary>
internal sealed class WebFunctionLogsUse : WebFunctionLogs
{
    internal WebFunctionLogsUse(ILogGroup logGroup) => LogGroup = logGroup;

    internal ILogGroup LogGroup { get; }
}

/// <summary>Uses Lambda's default log group.</summary>
internal sealed class WebFunctionLogsServiceDefault : WebFunctionLogs
{
}

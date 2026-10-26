using System;
using Amazon.CDK;
using Amazon.CDK.AWS.Logs;

namespace LayeredCraft.Cdk.Constructs.Models;

/// <summary>
/// Where a Web Function revision's logs go. A closed choice, so "supplied group" and "create a group" can
/// never be combined or contradict each other.
/// </summary>
/// <remarks>
/// A Web Function writes to two log groups. The centralized group (named
/// <c>/aws/lambda/web/{FunctionName}</c> unless supplied) lives in the stack Region and collects the logs
/// from every Region that serves requests. The companion group (the centralized name plus
/// <c>.local</c>) lives in the Region that served each request; Lambda creates it on first use when the
/// construct does not. Lambda Web Functions write to the companion group first and centralize from there.
/// </remarks>
public abstract class WebFunctionLogs
{
    private protected WebFunctionLogs()
    {
    }

    /// <summary>
    /// Creates the centralized log group named <c>/aws/lambda/web/{FunctionName}</c> and its companion
    /// group named <c>/aws/lambda/web/{FunctionName}.local</c>.
    /// </summary>
    /// <remarks>
    /// The centralized group defaults to <see cref="RetentionDays.TWO_WEEKS"/> retention and the companion
    /// group defaults to <see cref="RetentionDays.THREE_DAYS"/> retention, because the companion only holds
    /// the short-lived per-Region copy of the logs. Both groups use the same removal policy. Because the
    /// construct creates both groups in the stack Region, only a PerRegion or MultiRegion endpoint creates
    /// additional companion groups, and Lambda creates those outside the stack Region with no expiry.
    /// </remarks>
    /// <param name="retention">The retention for the created centralized group. Defaults to two weeks.</param>
    /// <param name="removalPolicy">The removal policy for both created groups. Defaults to destroy.</param>
    /// <param name="companionRetention">
    /// The retention for the created companion group. Defaults to three days.
    /// </param>
    public static WebFunctionLogs Create(
        RetentionDays? retention = null,
        RemovalPolicy? removalPolicy = null,
        RetentionDays? companionRetention = null)
        => new WebFunctionLogsCreate(
            retention ?? RetentionDays.TWO_WEEKS,
            removalPolicy ?? RemovalPolicy.DESTROY,
            companionRetention ?? RetentionDays.THREE_DAYS);

    /// <summary>Writes to existing log groups, passed by name to the revision.</summary>
    /// <remarks>
    /// The construct never creates a group in this mode. When <paramref name="companion"/> is omitted, the
    /// construct does not manage the companion group: Lambda creates it on first request with no expiry
    /// unless it already exists. When <paramref name="companion"/> is supplied it must be the group whose
    /// name is the centralized group's name plus <c>.local</c>.
    /// </remarks>
    /// <param name="logGroup">The existing centralized log group.</param>
    /// <param name="companion">
    /// The existing companion log group, or null to let Lambda create it on first request.
    /// </param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="logGroup"/> is null.</exception>
    /// <exception cref="ArgumentException">
    /// Thrown when both names are literal and <paramref name="companion"/> is not named
    /// <c>{logGroup}.local</c>.
    /// </exception>
    public static WebFunctionLogs Use(ILogGroup logGroup, ILogGroup? companion = null)
    {
        ArgumentNullException.ThrowIfNull(logGroup);
        ValidateCompanion(logGroup, companion);
        return new WebFunctionLogsUse(logGroup, companion);
    }

    /// <summary>
    /// Creates nothing and lets Lambda use its default groups. Both the centralized group
    /// (<c>/aws/lambda/web/{FunctionName}</c>) and its companion are service-managed and never expire.
    /// </summary>
    public static WebFunctionLogs ServiceDefault() => new WebFunctionLogsServiceDefault();

    private static void ValidateCompanion(ILogGroup logGroup, ILogGroup? companion)
    {
        if (companion is null)
            return;

        var name = logGroup.LogGroupName;
        var companionName = companion.LogGroupName;

        if (Token.IsUnresolved(name) || Token.IsUnresolved(companionName))
            return;

        var expected = $"{name}.local";
        if (!string.Equals(companionName, expected, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"The companion log group must be named '{expected}' to match the centralized log group, but was named '{companionName}'.",
                nameof(companion));
        }
    }
}

/// <summary>Creates managed log groups for the revision.</summary>
internal sealed class WebFunctionLogsCreate : WebFunctionLogs
{
    internal WebFunctionLogsCreate(
        RetentionDays retention,
        RemovalPolicy removalPolicy,
        RetentionDays companionRetention)
    {
        Retention = retention;
        RemovalPolicy = removalPolicy;
        CompanionRetention = companionRetention;
    }

    internal RetentionDays Retention { get; }

    internal RemovalPolicy RemovalPolicy { get; }

    internal RetentionDays CompanionRetention { get; }
}

/// <summary>Writes to existing log groups.</summary>
internal sealed class WebFunctionLogsUse : WebFunctionLogs
{
    internal WebFunctionLogsUse(ILogGroup logGroup, ILogGroup? companion)
    {
        LogGroup = logGroup;
        Companion = companion;
    }

    internal ILogGroup LogGroup { get; }

    internal ILogGroup? Companion { get; }
}

/// <summary>Uses Lambda's default log groups.</summary>
internal sealed class WebFunctionLogsServiceDefault : WebFunctionLogs
{
}

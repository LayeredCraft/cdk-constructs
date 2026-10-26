using Amazon.CDK;

namespace LayeredCraft.Cdk.Constructs.Models;

/// <summary>
/// The HTTP origin settings a consumer may choose when creating a Web Function CloudFront origin. Domain,
/// protocol (HTTPS only), and the origin access control id are owned by the origin factory and are
/// deliberately not expressible here.
/// </summary>
public record WebFunctionOriginProps
{
    /// <summary>An identifier for the origin, used to associate cache behaviors.</summary>
    public string? OriginId { get; init; }

    /// <summary>An optional path that CloudFront appends to the origin domain name.</summary>
    public string? OriginPath { get; init; }

    /// <summary>Headers CloudFront adds to origin requests.</summary>
    public IReadOnlyDictionary<string, string>? CustomHeaders { get; init; }

    /// <summary>The number of times CloudFront attempts to connect to the origin.</summary>
    public double? ConnectionAttempts { get; init; }

    /// <summary>The number of seconds CloudFront waits when establishing a connection.</summary>
    public Duration? ConnectionTimeout { get; init; }

    /// <summary>The number of seconds CloudFront waits for a response.</summary>
    public Duration? ReadTimeout { get; init; }

    /// <summary>The number of seconds CloudFront tries to keep a connection alive.</summary>
    public Duration? KeepaliveTimeout { get; init; }

    /// <summary>The number of seconds CloudFront waits for a response to complete.</summary>
    public Duration? ResponseCompletionTimeout { get; init; }

    /// <summary>Whether to enable CloudFront Origin Shield.</summary>
    public bool? OriginShieldEnabled { get; init; }

    /// <summary>The Region to use for CloudFront Origin Shield.</summary>
    public string? OriginShieldRegion { get; init; }
}

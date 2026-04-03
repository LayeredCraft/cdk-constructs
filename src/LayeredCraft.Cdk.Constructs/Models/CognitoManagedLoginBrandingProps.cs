namespace LayeredCraft.Cdk.Constructs.Models;

/// <summary>
/// Configuration properties for a single branding asset uploaded to Cognito Managed Login.
/// Asset bytes are read from <see cref="FilePath"/> at synth time and base64-encoded before
/// being passed to CloudFormation.
/// </summary>
public interface ICognitoManagedLoginBrandingAssetProps
{
    /// <summary>
    /// The asset category as defined by Cognito (e.g. <c>FORM_LOGO</c>, <c>BROWSER_FAVICON</c>).
    /// </summary>
    string Category { get; }

    /// <summary>
    /// The color mode the asset applies to. Accepted values are <c>LIGHT</c> and <c>DARK</c>.
    /// </summary>
    string ColorMode { get; }

    /// <summary>
    /// The file extension of the asset (e.g. <c>png</c>, <c>svg</c>, <c>ico</c>).
    /// </summary>
    string Extension { get; }

    /// <summary>
    /// The absolute or relative path to the asset file on the machine running <c>cdk synth</c> / <c>cdk deploy</c>.
    /// The file is read and base64-encoded at synth time.
    /// </summary>
    string FilePath { get; }
}

/// <summary>
/// Default implementation of <see cref="ICognitoManagedLoginBrandingAssetProps"/>.
/// </summary>
public sealed record CognitoManagedLoginBrandingAssetProps(
    string Category,
    string ColorMode,
    string Extension,
    string FilePath) : ICognitoManagedLoginBrandingAssetProps;

/// <summary>
/// Configuration properties for Cognito Managed Login branding associated with an app client.
/// </summary>
public interface ICognitoManagedLoginBrandingProps
{
    /// <summary>
    /// A JSON string matching the Cognito Managed Login branding settings schema.
    /// The document is deserialized at synth time and passed directly to the
    /// <c>AWS::Cognito::ManagedLoginBranding</c> CloudFormation resource.
    /// </summary>
    string SettingsJson { get; }

    /// <summary>
    /// Optional collection of branding assets (logos, favicons) to upload alongside the settings.
    /// When <see langword="null"/> or empty no assets are included.
    /// </summary>
    IReadOnlyCollection<ICognitoManagedLoginBrandingAssetProps>? Assets { get; }
}

/// <summary>
/// Default implementation of <see cref="ICognitoManagedLoginBrandingProps"/>.
/// </summary>
public sealed record CognitoManagedLoginBrandingProps(
    string SettingsJson,
    IReadOnlyCollection<ICognitoManagedLoginBrandingAssetProps>? Assets = null) : ICognitoManagedLoginBrandingProps;

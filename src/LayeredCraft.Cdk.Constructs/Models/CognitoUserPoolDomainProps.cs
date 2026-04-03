using Amazon.CDK.AWS.CertificateManager;

namespace LayeredCraft.Cdk.Constructs.Models;


public interface ICognitoUserPoolDomainProps
{
    string? CognitoDomainPrefix { get; }

    string? DomainName { get; }

    string? AuthSubDomain { get; }

    CognitoManagedLoginVersion ManagedLoginVersion { get; }

    bool CreateRoute53Record { get; }

    /// <summary>
    /// An existing ACM certificate to use for the custom domain.
    /// When provided, the construct skips certificate creation and uses this certificate directly.
    /// Cognito custom domains require the certificate to be in <c>us-east-1</c>.
    /// If <see langword="null"/> and a custom domain is configured, a new certificate is created
    /// in the stack's region (valid when the stack is already deployed to <c>us-east-1</c>).
    /// </summary>
    ICertificate? Certificate { get; }
}

public sealed record CognitoUserPoolDomainProps : ICognitoUserPoolDomainProps
{
    public string? CognitoDomainPrefix { get; init; }

    public string? DomainName { get; init; }

    public string? AuthSubDomain { get; init; }

    public CognitoManagedLoginVersion ManagedLoginVersion { get; init; } =
        CognitoManagedLoginVersion.ManagedLogin;

    public bool CreateRoute53Record { get; init; } = true;

    /// <inheritdoc />
    public ICertificate? Certificate { get; init; }
}

public enum CognitoManagedLoginVersion
{
    ClassicHostedUi,
    ManagedLogin,
}
using Amazon.CDK.AWS.CertificateManager;

namespace LayeredCraft.Cdk.Constructs.Models;


public interface ICognitoUserPoolDomainProps
{
    string? CognitoDomainPrefix { get; }

    string? DomainName { get; }

    string? AuthSubDomain { get; }

    CognitoManagedLoginVersion ManagedLoginVersion { get; }

    bool CreateRoute53Record { get; }
}

public sealed record CognitoUserPoolDomainProps : ICognitoUserPoolDomainProps
{
    public string? CognitoDomainPrefix { get; init; }

    public string? DomainName { get; init; }

    public string? AuthSubDomain { get; init; }

    public CognitoManagedLoginVersion ManagedLoginVersion { get; init; } =
        CognitoManagedLoginVersion.ManagedLogin;

    public bool CreateRoute53Record { get; init; } = true;
}

public enum CognitoManagedLoginVersion
{
    ClassicHostedUi,
    ManagedLogin,
}
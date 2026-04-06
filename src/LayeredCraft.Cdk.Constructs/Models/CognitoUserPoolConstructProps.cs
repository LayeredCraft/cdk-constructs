using Amazon.CDK;
using Amazon.CDK.AWS.Cognito;
using Amazon.CDK.AWS.Lambda;

namespace LayeredCraft.Cdk.Constructs.Models;

public interface ICognitoUserPoolConstructProps
{
    string UserPoolName { get; }

    bool SelfSignUpEnabled { get; }

    RemovalPolicy RemovalPolicy { get; }

    Mfa Mfa { get; }

    MfaSecondFactor? MfaSecondFactor { get; }

    int PasswordMinLength { get; }
    
    IReadOnlyList<ICognitoResourceServerProps> ResourceServers { get; }
    IReadOnlyList<ICognitoUserPoolAppClientProps> AppClients { get; }
    ICognitoUserPoolDomainProps? Domain { get; }
    IReadOnlyCollection<ICognitoUserPoolGroupProps>? Groups { get; }
    IFunction? PostConfirmationTrigger { get; }
}

public sealed record CognitoUserPoolConstructProps : ICognitoUserPoolConstructProps
{
    public required string UserPoolName { get; init; }

    public bool SelfSignUpEnabled { get; init; } = true;

    public RemovalPolicy RemovalPolicy { get; init; } = RemovalPolicy.DESTROY;

    public Mfa Mfa { get; init; } = Mfa.OFF;

    public MfaSecondFactor? MfaSecondFactor { get; init; }

    public int PasswordMinLength { get; init; } = 12;
    public IReadOnlyList<ICognitoResourceServerProps> ResourceServers { get; init; } = [];
    public IReadOnlyList<ICognitoUserPoolAppClientProps> AppClients { get; init; } = [];
    public ICognitoUserPoolDomainProps? Domain { get; init; }
    public IReadOnlyCollection<ICognitoUserPoolGroupProps>? Groups { get; init; } = [];
    public IFunction? PostConfirmationTrigger { get; init; } = null;
}

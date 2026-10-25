using Amazon.CDK;
using WebFunctionSandbox;

// CDK entry point. Configuration comes exclusively from SANDBOX_* environment variables (see
// SandboxConfiguration) - the app never reads or infers an ambient account or Region, so an operator must state
// the sandbox target explicitly. The AWS-mutating behaviour lives in the shell scripts, never here.
//
// The output directory follows the CDK CLI protocol (CDK_OUTDIR) when set, which also lets the scripts run a
// synthesis offline without a version-compatible CDK CLI.
var outdir = System.Environment.GetEnvironmentVariable("CDK_OUTDIR");
var app = string.IsNullOrWhiteSpace(outdir)
    ? new App()
    : new App(new AppProps { Outdir = outdir });

SandboxConfiguration configuration;
try
{
    configuration = SandboxConfiguration.FromEnvironment();
}
catch (SandboxConfigurationException exception)
{
    Console.Error.WriteLine(exception.Message);
    return 1;
}

var naming = new SandboxNaming(configuration);

var stackProps = new StackProps
{
    Env = new Amazon.CDK.Environment
    {
        Account = configuration.Account,
        Region = configuration.Region,
    },
    Description = "Temporary LayeredCraft Web Function sandbox resources (T7c validation).",
};

ArtifactsStack? artifacts = null;
ExternalStack? external = null;

if (configuration.BuildsArtifacts)
    artifacts = new ArtifactsStack(app, naming.ArtifactsStackName, stackProps, configuration, naming);

if (configuration.BuildsExternal)
    external = new ExternalStack(app, naming.ExternalStackName, stackProps, configuration, naming);

if (configuration.BuildsApp)
{
    // BuildsApp always implies BuildsArtifacts and BuildsExternal, so the support stacks are present.
    var appStack = new AppStack(app, naming.AppStackName, stackProps, configuration, naming, artifacts!.ArtifactsBucket);

    // Explicit cross-stack dependencies so CloudFormation never creates the workloads before the buckets they
    // read from exist. The scripts also order the deploys, but the CDK graph is the source of truth.
    appStack.AddStackDependency(artifacts!);
    appStack.AddStackDependency(external!);

    if (configuration.HasPlaceholderVersions)
    {
        Console.Error.WriteLine(
            "WARNING: the app stack was synthesized with placeholder artifact versions "
            + "(SANDBOX_ALLOW_PLACEHOLDER_VERSIONS). This synthesis must never be deployed; "
            + "deploy the app stack with the real uploaded object versions.");
    }
}

app.Synth();
return 0;
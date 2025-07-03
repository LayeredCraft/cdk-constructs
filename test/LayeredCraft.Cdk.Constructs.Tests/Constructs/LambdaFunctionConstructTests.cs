using Amazon.CDK;
using Amazon.CDK.Assertions;
using AwesomeAssertions;
using LayeredCraft.Cdk.Constructs.Constructs;
using LayeredCraft.Cdk.Constructs.Models;
using LayeredCraft.Cdk.Constructs.Tests.TestKit.Attributes;

namespace LayeredCraft.Cdk.Constructs.Tests.Constructs;

[Collection("CDK Tests")]
public class LambdaFunctionConstructTests
{
    [Theory]
    [LambdaFunctionConstructAutoData]
    public void Construct_ShouldCreateLambdaFunction(LambdaFunctionConstructProps props)
    {
        var app = new App();
        var stack = new Stack(app, "test-stack", new StackProps
        {
            Env = new Amazon.CDK.Environment { Account = "123456789012", Region = "us-east-1" }
        });
        
        var construct = new LambdaFunctionConstruct(stack, "test-construct", props);
        var template = Template.FromStack(stack);

        construct.Should().NotBeNull();
        template.HasResourceProperties("AWS::Lambda::Function", Match.ObjectLike(new Dictionary<string, object>
        {
            { "FunctionName", $"{props.FunctionName}-{props.FunctionSuffix}" },
            { "Runtime", "provided.al2023" },
            { "Handler", "bootstrap" },
            { "MemorySize", 1024 },
            { "Timeout", 6 }
        }));
    }

    [Theory]
    [LambdaFunctionConstructAutoData]
    public void Construct_ShouldCreateIAMRole(LambdaFunctionConstructProps props)
    {
        var app = new App();
        var stack = new Stack(app, "test-stack", new StackProps
        {
            Env = new Amazon.CDK.Environment { Account = "123456789012", Region = "us-east-1" }
        });
        
        _  = new LambdaFunctionConstruct(stack, "test-construct", props);
        var template = Template.FromStack(stack);

        template.HasResourceProperties("AWS::IAM::Role", Match.ObjectLike(new Dictionary<string, object>
        {
            { "RoleName", props.RoleName }
        }));
    }

    [Theory]
    [LambdaFunctionConstructAutoData]
    public void Construct_ShouldCreateIAMPolicy(LambdaFunctionConstructProps props)
    {
        var app = new App();
        var stack = new Stack(app, "test-stack", new StackProps
        {
            Env = new Amazon.CDK.Environment { Account = "123456789012", Region = "us-east-1" }
        });
        
        _ = new LambdaFunctionConstruct(stack, "test-construct", props);
        var template = Template.FromStack(stack);

        template.HasResourceProperties("AWS::IAM::Policy", Match.ObjectLike(new Dictionary<string, object>
        {
            { "PolicyName", props.PolicyName }
        }));
    }

    [Theory]
    [LambdaFunctionConstructAutoData]
    public void Construct_ShouldIncludeCloudWatchLogsPermissions(LambdaFunctionConstructProps props)
    {
        var app = new App();
        var stack = new Stack(app, "test-stack", new StackProps
        {
            Env = new Amazon.CDK.Environment { Account = "123456789012", Region = "us-east-1" }
        });
        
        _ = new LambdaFunctionConstruct(stack, "test-construct", props);
        var template = Template.FromStack(stack);

        // Verify the policy exists with correct name
        template.HasResourceProperties("AWS::IAM::Policy", Match.ObjectLike(new Dictionary<string, object>
        {
            { "PolicyName", props.PolicyName },
            { "PolicyDocument", Match.ObjectLike(new Dictionary<string, object>
            {
                { "Statement", Match.ArrayWith([
                    // First statement: logs:CreateLogStream, logs:CreateLogGroup, logs:TagResource
                    Match.ObjectLike(new Dictionary<string, object>
                    {
                        { "Effect", "Allow" },
                        { "Action", Match.ArrayWith([
                            "logs:CreateLogStream",
                            "logs:CreateLogGroup", 
                            "logs:TagResource"
                        ]) },
                        { "Resource", $"arn:aws:logs:us-east-1:123456789012:log-group:/aws/lambda/{props.FunctionName}*:*" }
                    }),
                    // Second statement: logs:PutLogEvents
                    Match.ObjectLike(new Dictionary<string, object>
                    {
                        { "Effect", "Allow" },
                        { "Action", "logs:PutLogEvents" },
                        { "Resource", $"arn:aws:logs:us-east-1:123456789012:log-group:/aws/lambda/{props.FunctionName}*:*:*" }
                    })
                ]) }
            }) }
        }));
    }

    [Theory]
    [LambdaFunctionConstructAutoData]
    public void Construct_ShouldSetEnvironmentVariables(LambdaFunctionConstructProps props)
    {
        var app = new App();
        var stack = new Stack(app, "test-stack", new StackProps
        {
            Env = new Amazon.CDK.Environment { Account = "123456789012", Region = "us-east-1" }
        });
        
        _ = new LambdaFunctionConstruct(stack, "test-construct", props);
        var template = Template.FromStack(stack);

        template.HasResourceProperties("AWS::Lambda::Function", Match.ObjectLike(new Dictionary<string, object>
        {
            { "Environment", Match.ObjectLike(new Dictionary<string, object>
            {
                { "Variables", Match.ObjectLike(props.EnvironmentVariables.ToDictionary(kvp => kvp.Key, kvp => (object)kvp.Value)) }
            }) }
        }));
    }

    [Theory]
    [LambdaFunctionConstructAutoData(includeOtelLayer: true)]
    public void Construct_ShouldIncludeOtelLayerWhenEnabled(LambdaFunctionConstructProps props)
    {
        var app = new App();
        var stack = new Stack(app, "test-stack", new StackProps
        {
            Env = new Amazon.CDK.Environment { Account = "123456789012", Region = "us-east-1" }
        });
        
        _ = new LambdaFunctionConstruct(stack, "test-construct", props);
        var template = Template.FromStack(stack);

        template.HasResourceProperties("AWS::Lambda::Function", Match.ObjectLike(new Dictionary<string, object>
        {
            { "TracingConfig", new Dictionary<string, object> { { "Mode", "Active" } } },
            { "Layers", new object[] { Match.StringLikeRegexp(".*aws-otel-collector.*") } }
        }));
    }

    [Theory]
    [LambdaFunctionConstructAutoData(includeOtelLayer: false)]
    public void Construct_ShouldDisableTracingWhenOtelLayerDisabled(LambdaFunctionConstructProps props)
    {
        var app = new App();
        var stack = new Stack(app, "test-stack", new StackProps
        {
            Env = new Amazon.CDK.Environment { Account = "123456789012", Region = "us-east-1" }
        });
        
        var construct = new LambdaFunctionConstruct(stack, "test-construct", props);
        var template = Template.FromStack(stack);

        // Verify construct was created successfully
        construct.Should().NotBeNull();
        
        // Verify that there is 1 Lambda function (main function only, no log retention function)
        template.ResourceCountIs("AWS::Lambda::Function", 1);
        
        // Verify that the Lambda function has no TracingConfig (tracing disabled)
        template.HasResourceProperties("AWS::Lambda::Function", Match.ObjectLike(new Dictionary<string, object>
        {
            { "FunctionName", $"{props.FunctionName}-{props.FunctionSuffix}" }
        }));
        
        // Verify that TracingConfig is not present (tracing disabled when OTEL layer is disabled)
        template.HasResourceProperties("AWS::Lambda::Function", Match.Not(Match.ObjectLike(new Dictionary<string, object>
        {
            { "TracingConfig", Match.AnyValue() }
        })));
        
        // Verify that Layers property is not present (no OTEL layer when disabled)
        template.HasResourceProperties("AWS::Lambda::Function", Match.Not(Match.ObjectLike(new Dictionary<string, object>
        {
            { "Layers", Match.AnyValue() }
        })));
    }

    [Theory]
    [LambdaFunctionConstructAutoData]
    public void Construct_ShouldCreateVersionAndAlias(LambdaFunctionConstructProps props)
    {
        var app = new App();
        var stack = new Stack(app, "test-stack", new StackProps
        {
            Env = new Amazon.CDK.Environment { Account = "123456789012", Region = "us-east-1" }
        });
        
        _ = new LambdaFunctionConstruct(stack, "test-construct", props);
        var template = Template.FromStack(stack);

        // Verify Version resource exists (DeletionPolicy is at resource level, not in Properties)
        template.ResourceCountIs("AWS::Lambda::Version", 1);

        template.HasResourceProperties("AWS::Lambda::Alias", Match.ObjectLike(new Dictionary<string, object>
        {
            { "Name", "live" }
        }));
    }

    [Theory]
    [LambdaFunctionConstructAutoData]
    public void Construct_ShouldAddPermissionsToAllTargets(LambdaFunctionConstructProps props)
    {
        var app = new App();
        var stack = new Stack(app, "test-stack", new StackProps
        {
            Env = new Amazon.CDK.Environment { Account = "123456789012", Region = "us-east-1" }
        });
        
        _ = new LambdaFunctionConstruct(stack, "test-construct", props);
        var template = Template.FromStack(stack);

        if (props.Permissions.Count == 0) return;
        var expectedPermissionCount = props.Permissions.Count * 3; // function + version + alias
        template.ResourceCountIs("AWS::Lambda::Permission", expectedPermissionCount);
    }

    [Theory]
    [LambdaFunctionConstructAutoData(includePermissions: false)]
    public void Construct_ShouldHandleEmptyPermissionsList(LambdaFunctionConstructProps props)
    {
        var app = new App();
        var stack = new Stack(app, "test-stack", new StackProps
        {
            Env = new Amazon.CDK.Environment { Account = "123456789012", Region = "us-east-1" }
        });
        
        _ = new LambdaFunctionConstruct(stack, "test-construct", props);
        var template = Template.FromStack(stack);

        props.Permissions.Should().BeEmpty();
        template.ResourceCountIs("AWS::Lambda::Permission", 0);
    }

    [Theory]
    [LambdaFunctionConstructAutoData]
    public void Construct_ShouldCreateLogGroupWithRetention(LambdaFunctionConstructProps props)
    {
        var app = new App();
        var stack = new Stack(app, "test-stack", new StackProps
        {
            Env = new Amazon.CDK.Environment { Account = "123456789012", Region = "us-east-1" }
        });
        
        _ = new LambdaFunctionConstruct(stack, "test-construct", props);
        var template = Template.FromStack(stack);

        // Verify the LogGroup is explicitly created with correct retention
        template.HasResourceProperties("AWS::Logs::LogGroup", Match.ObjectLike(new Dictionary<string, object>
        {
            { "LogGroupName", $"/aws/lambda/{props.FunctionName}-{props.FunctionSuffix}" },
            { "RetentionInDays", 14 }
        }));
    }
}
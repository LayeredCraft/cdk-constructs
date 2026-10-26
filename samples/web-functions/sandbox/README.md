# Web Function sandbox

A temporary AWS sandbox that exercises the LayeredCraft Web Function constructs end to end. It exists to
run the approved design's AWS acceptance matrix (sections 3.1-3.8) for `LayeredCraft.Cdk.Constructs`
against a **selected existing account**, using its own fixed namespace and ownership tags, and to be torn
down afterwards.

> **This sandbox is for validation only.** It creates real, billable AWS resources in a shared account. It
> only ever creates, updates, and deletes resources inside its own `lccdk-wf-sandbox-*` namespace and never
> touches unrelated resources. Nothing here is intended for production use.

The sandbox app is a small C# CDK application that consumes the real library project (`WebFunctionConstruct`,
`WebFunctionEndpointConstruct`, `WebFunctionOrigin`, ...) - it does **not** re-implement any construct
behaviour with raw CloudFormation. It is intentionally excluded from the library solution and CI.

## What it deploys

Three stacks. Every stack name carries the fixed sandbox namespace `lccdk-wf-sandbox` **and** the sandbox id
(`lccdk-wf-sandbox-<id>-<role>`), and every stack carries the ownership tags `lc-webfunction-sandbox-owner`
and `lc-webfunction-sandbox-id`. Buckets, functions and roles keep the shorter `<id>-...` prefix.

| Stack | Resources | Purpose |
| --- | --- | --- |
| `lccdk-wf-sandbox-<id>-artifacts` | versioned, SSE-S3, private S3 bucket | Owned artifact bucket used with the library's default `Automatic` code-bucket policy |
| `lccdk-wf-sandbox-<id>-external` | versioned, SSE-S3, private bucket; two IAM roles | Pre-provisioned "external" bucket (`ExternallyManaged` policy) and the imported caller roles |
| `lccdk-wf-sandbox-<id>-app` | 3 Web Functions, 4 endpoints, 2 CloudFront distributions (3 with the second distribution), 2 OACs, 5 IAM roles, 1 IAM policy, 3 log groups | Next.js standalone, ASP.NET Core (with a second endpoint), and Blazor Static SSR |

The app stack declares an explicit CDK dependency on the artifacts and external stacks, and every deploy
synthesizes the **complete** app (all three stacks) so the support stacks keep the cross-stack exports the
app stack imports. Deploying a reduced app is what breaks repeat deployments, so it is never done.

Workloads:

- **Next.js standalone** - `IamAuth` default endpoint behind a CloudFront distribution using origin access
  control (`WebFunctionOrigin.WithOriginAccessControl`). Dynamic document behaviour is uncached;
  `/_next/static/*` is cached.
- **ASP.NET Core** (Node.js `execve` shim entrypoint) - `IamAuth` default endpoint plus an `alt` endpoint,
  using the `ExternallyManaged` code-bucket policy.
- **Blazor Static SSR** - `IamAuth` default endpoint behind an OAC distribution; `/_framework/*` and
  `/_content/*` are cached.

## Prerequisites

- The sandbox is a .NET 10 application. Its local `global.json` rolls the SDK forward so the library's
  `net11.0` preview target can be referenced.
- **A CDK CLI that understands cloud assembly schema 55** (the library's `Amazon.CDK.Lib` 2.274.0 output).
  This needs CLI **2.1145.0 or newer**. The scripts pin `npx --yes aws-cdk@2.1145.0` in one place
  (`CDK_CLI_VERSION` in `common.sh`); override the whole command with `SANDBOX_CDK` (for example
  `SANDBOX_CDK="npx --yes aws-cdk@2.1146.1"`) if you need a different build.
- **CDK bootstrap is required** in the target account/Region before the first deploy
  (`npx --yes aws-cdk@2.1145.0 bootstrap aws://<account>/<region>`), because the app uses the modern
  bootstrap (asset publishing roles + `cdk-hnb659fds-*` buckets).
- **`aws` CLI v2** (tested against 2.37.x), `curl` built with `--aws-sigv4`, `python3`, `zip`/`unzip`, and
  the .NET SDK.
  - The smoke tests prefer the `aws lambda-web get-resource-policy` / `get-web-function` subcommands. If your
    CLI does not ship those subcommands (many 2.x builds do not), the affected checks are reported as
    **BLOCKED** with the missing-API reason rather than being reported as passing.
- Build the workload artifacts first with `../build-artifacts.sh` (they land in `../build`).

## Required AWS permissions

The operator identity needs, scoped to the sandbox account and Region:

- CloudFormation: create/update/delete stacks, `get-template`, `describe-stack-resources`.
- S3: create/delete versioned buckets, put/get/head objects, get/put bucket policy, get bucket versioning,
  delete object versions and delete markers.
- IAM: create/delete roles and inline policies, `PassRole`, `GetRole`, `ListRolePolicies`, `GetRolePolicy`.
- Lambda (Web Functions): create/update/delete functions, revisions and endpoints, and (where the API is
  available) read resource policies.
- CloudFront: create/update/delete distributions and origin access controls.
- CloudWatch Logs: create/delete the `/aws/lambda/web/*` log groups.
- `sts:AssumeRole` on the sandbox caller roles (they are created by the sandbox).

## Safety model (why you cannot hit production by accident)

- **Explicit target only.** `SANDBOX_ACCOUNT`, `SANDBOX_REGION`, and `SANDBOX_ID` are required. Nothing is
  inferred from your ambient profile or default Region.
- **Opt-in to AWS.** Every script that touches AWS requires `SANDBOX_ALLOW_AWS=1`, and every mutating script
  additionally requires `SANDBOX_ACKNOWLEDGE_SHARED_ACCOUNT=1` (you accept creating temporary sandbox
  resources in the selected existing account while leaving unrelated resources untouched); without them they
  exit before making a call.
- **Live identity verification.** Before any mutation the scripts call STS `get-caller-identity` and refuse
  to continue unless the live account and Region match the declared sandbox exactly. Every AWS call is pinned
  with `--region`, and a disagreeing ambient `AWS_REGION`/`AWS_DEFAULT_REGION` aborts, so an inherited profile
  or environment variable cannot silently retarget a command.
- **Namespaced, tagged, verified names.** Stack names carry the fixed `lccdk-wf-sandbox` namespace and the
  sandbox id, and every stack carries an ownership tag. Before a stack is updated or deleted the scripts read
  its tags (and, when present, its `SandboxId` output) and fail closed if ownership cannot be proven - a
  foreign stack is never treated as sandbox-owned.
- **Ambiguity is fatal.** A stack describe that fails for any reason other than "does not exist" (throttling,
  expired credentials, `AccessDenied`) aborts the run rather than assuming the stack is gone.
- **Destructive double-gate.** `destroy.sh` requires `--yes`, `SANDBOX_CONFIRM_DESTROY=<SANDBOX_ID>`, the
  shared-account acknowledgement, and a verified sandbox-owned target, checked before any AWS call. It only
  ever empties versioned objects from stack-owned buckets and lets CloudFormation delete the buckets, and it
  removes the local artifact manifest only after every stack is confirmed gone.
- **No script deploys or destroys as a side effect.** Nothing runs on build or import; every action is an
  explicit invocation.

## Configuration

| Variable | Required | Meaning |
| --- | --- | --- |
| `SANDBOX_ACCOUNT` | yes | 12-digit sandbox account id |
| `SANDBOX_REGION` | yes | sandbox Region, e.g. `us-east-1` |
| `SANDBOX_ID` | yes | 3-16 char lowercase namespace used for all names |
| `SANDBOX_ALLOW_AWS` | for AWS ops | set to `1` to opt in to AWS calls |
| `SANDBOX_ACKNOWLEDGE_SHARED_ACCOUNT` | for AWS mutations | set to `1` to confirm you accept creating temporary sandbox resources in the selected existing account without modifying unrelated resources |
| `SANDBOX_CONFIRM_DESTROY` | for destroy | must equal `SANDBOX_ID` |
| `SANDBOX_COMPONENTS` | no | `all` (default), or a comma list of `artifacts`, `external`, `app` |
| `SANDBOX_SECOND_DISTRIBUTION` | no | `1`/`true` adds a second Next.js distribution (resource-policy lifecycle) |
| `SANDBOX_CROSS_ACCOUNT_CALLER_ROLE_ARN` | no | enables the optional cross-account grant |
| `SANDBOX_ARTIFACTS_PREFIX` | no | S3 key prefix for uploaded artifacts (default: `SANDBOX_ID`) |
| `SANDBOX_CDK` | no | full CDK CLI command override instead of the pinned `npx --yes aws-cdk@<CDK_CLI_VERSION>` |
| `SANDBOX_ALLOW_PLACEHOLDER_VERSIONS` | internal | synthesis-only escape hatch used by the deploy/destroy lifecycle; never set it for a real deploy |

## Step-by-step run

```bash
export SANDBOX_ACCOUNT=111122223333
export SANDBOX_REGION=us-east-1
export SANDBOX_ID=wfsbx

# 1. Prove the target is safe (validates formats; add --check-aws to verify the live identity).
./validate-env.sh

# 2. Build the workload artifacts once.
../build-artifacts.sh

# 3. Synthesize locally (no AWS calls) to inspect the templates. Pass --version <label> after a revision
#    has been uploaded, or set the SANDBOX_<WORKLOAD>_OBJECT_VERSION variables directly for a one-off synth.
./synth.sh --version v1

# 4. Deploy v1 end to end (support stacks, upload, app stack).
SANDBOX_ALLOW_AWS=1 SANDBOX_ACKNOWLEDGE_SHARED_ACCOUNT=1 ./deploy.sh

# 5. Run the acceptance smoke tests (record the machine-readable summary as evidence).
SANDBOX_ALLOW_AWS=1 ./smoke.sh

# 6. Update v1 -> v2 and re-check.
SANDBOX_ALLOW_AWS=1 SANDBOX_ACKNOWLEDGE_SHARED_ACCOUNT=1 ./update-to-v2.sh
SANDBOX_ALLOW_AWS=1 ./smoke.sh

# 7. Tear everything down.
SANDBOX_ALLOW_AWS=1 SANDBOX_ACKNOWLEDGE_SHARED_ACCOUNT=1 SANDBOX_CONFIRM_DESTROY=wfsbx ./destroy.sh --yes
```

### Scripts

| Script | What it does |
| --- | --- |
| `validate-env.sh` | Validates the sandbox configuration (and optionally the live AWS identity) |
| `synth.sh` | Synthesizes the CDK app locally; never deploys |
| `upload-artifacts.sh` | Uploads a build revision to the versioned buckets and records object versions |
| `deploy.sh` | Ordered deploy: support stacks, upload, app stack |
| `update-to-v2.sh` | Uploads and deploys the `v2` revision (the update path) |
| `smoke.sh` | AWS acceptance smoke tests (sections 3.1-3.8) with explicit expected results |
| `destroy.sh` | Empties stack-owned buckets (object versions + delete markers only), destroys the stacks, deletes log groups |
| `run-workflow.sh` | Convenience wrapper chaining validate -> synth -> deploy -> smoke |

`upload-artifacts.sh` records the bucket/key/version identity of each revision in
`build/artifacts.json` (git-ignored) so a deployment can be reproduced without rebuilding.

## Cost

Each run creates a small number of Lambda Web Functions, two or three CloudFront distributions, S3 storage
for the artifacts, and CloudWatch log groups. Cost is dominated by the CloudFront distributions
(per-hour) and any request/transfer volume. Tear down promptly; `destroy.sh` removes everything.

## Recording evidence (T7c)

`smoke.sh` prints a PASS/FAIL/SKIP line per check and writes `build/smoke-summary.json`. Capture both, plus
the CloudFormation outputs from the app stack, as the acceptance evidence. Authorization checks retry with
backoff so a transient 403 during permission propagation is reported separately from a persistent
authorization failure.

## Synthesis limitations

`./synth.sh` and `dotnet build` verify compilation and provoke synthesis only. They **do not** prove AWS
behaviour. In particular, synthesis cannot validate: actual IAM/CloudFront permission propagation timing,
`execve` behaviour under the real Lambda runtime, CloudFront cache behaviour, or the live resource-policy
API. Those are only established by deploying (T7c). The CDK CLI also emits `F3006` warnings for the new
`AWS::Lambda::WebFunction*` resource types and a cross-stack reference-strength note; both are expected.
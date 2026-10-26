# Web Function workload samples

Deployment-ready sample workloads for the [`WebFunctionConstruct`](../../src/LayeredCraft.Cdk.Constructs/WebFunctionConstruct.cs)
(`AWS::Lambda::WebFunction`). Each app produces an S3 artifact that matches the approved
runtime contract in [`docs/designs/lambda-web-functions-integration.md`](../../docs/designs/lambda-web-functions-integration.md)
(revision 4, section 16).

These samples are **not** part of the library solution (`LayeredCraft.Cdk.Constructs.slnx`)
and are never built or packed by CI. They exist to prove that real Next.js, ASP.NET Core,
and Blazor Static SSR workloads package into the runtime's expected layout and run under
the Node.js `execve` shim.

## Layout

```
samples/web-functions/
  apps/nextjs/          Next.js standalone app (Node.js runtime, no shim)
  apps/aspnetcore/      ASP.NET Core minimal API (execve shim + self-contained apphost)
  apps/blazor/          Blazor Web App, Static SSR only (execve shim + self-contained apphost)
  tools/mock-runtime-api.mjs   Local stand-in for the Lambda runtime API (/2025-09-01/ready)
  tools/test-tooling.sh        Regression tests for build-artifacts.sh path/cleanup handling
  build-artifacts.sh    Builds deployment ZIPs into build/
  smoke-artifacts.sh    Local HTTP smoke tests against the built ZIPs
```

## Prerequisites

- Node.js 24+ (samples built with v26) and npm 11+
- .NET SDK 10.0 (pinned via `samples/web-functions/global.json`; `rollForward=latestFeature`)
- Docker (required for the local smoke tests; the artifacts build without it)
- `zip`/`unzip`

Building the artifacts requires **no AWS credentials and makes no AWS calls**.

## Build artifacts

```bash
cd samples/web-functions
./build-artifacts.sh                 # all three workloads + v2 variants
./build-artifacts.sh --clean         # remove generated artifacts first (repeatable builds)
./build-artifacts.sh --only blazor   # one workload
./build-artifacts.sh --build-id abc123   # override the baked build id
./build-artifacts.sh --out /tmp/out  # custom output directory (relative paths allowed)
```

By default the build id is `git rev-parse --short=12 HEAD`. Outputs land in
`samples/web-functions/build/`:

| Artifact | Workload |
| --- | --- |
| `nextjs.zip` | Next.js standalone |
| `aspnetcore.zip` | ASP.NET Core |
| `blazor.zip` | Blazor Static SSR |
| `nextjs-v2.zip`, `aspnetcore-v2.zip`, `blazor-v2.zip` | Same apps with a `-v2` build id, for exercising revision updates |

`--clean` only removes content the script generates (`*.zip`, `stage/`, `.smoke/`) inside the
output directory; it never deletes the output directory itself or unrelated files in it, so
`--out` may safely point at an existing directory (including the sample root).

Artifacts are built for **`linux/arm64`**, the only architecture Web Functions support. The
Next.js build installs its optional native dependencies for that platform
(`npm ci --os=linux --cpu=arm64 --libc=glibc`) so the bundle carries `sharp-linux-arm64`
binaries rather than the build host's. The artifact therefore targets the Lambda
`linux/arm64` runtime and is **not** host-independent.

Builds are **repeatable but not byte-for-byte reproducible**: ZIP records file modification
times and stores entries in directory order, and the Next.js build stamps its own generated
identifiers. Two runs from the same source produce functionally equivalent but not
byte-identical archives.

## Artifact layout

All ZIPs are rooted at `/var/task` (no top-level wrapper directory).

**Next.js** (Node.js is the runtime, so there is no shim):

```
index.js            entry point (shim: sets HOSTNAME/PORT from AWS_LAMBDA_HTTP_ENDPOINT, SIGTERM handling, requires ./server.js)
server.js           Next.js standalone server
.next/              server + static output (.next/static included)
public/             static assets (e.g. sample.txt)
node_modules/       standalone server dependencies (linux-arm64 native modules)
package.json
build-id.txt        build id consumed by the SSR page
```

The sample does not use the Next.js image optimizer; `images.unoptimized` is set so the
revision never invokes the sharp/libvips image pipeline. (The traced `sharp` packages are
still shipped, pinned to `linux-arm64`, so the layout stays a faithful standalone bundle.)

**ASP.NET Core / Blazor** (single file `index.js` is the execve shim):

```
index.js            process.execve("/var/task/server", ["/var/task/server"], process.env)
server              self-contained apphost (executable bit preserved: -rwxr-xr-x)
server.dll          managed entry assembly
server.deps.json, server.runtimeconfig.json, *.dll, *.so   self-contained runtime assets
wwwroot/            (Blazor only) app.css + _framework/blazor.web.js
build-id.txt
```

Both .NET apps bind Kestrel to `http://$AWS_LAMBDA_HTTP_ENDPOINT` and POST `{}` to
`http://$AWS_LAMBDA_RUNTIME_API/2025-09-01/ready` once listening (skipped when the
variable is absent), which the runtime requires to leave `Runtime.InitTimeout`.
The Blazor app sets `ContentRootPath` to `AppContext.BaseDirectory` so published
`wwwroot` assets resolve at runtime.

## Local validation

```bash
cd samples/web-functions
./smoke-artifacts.sh                 # validate all built artifacts (v1 and v2)
./smoke-artifacts.sh --only nextjs   # one artifact (accepts nextjs-v2, etc.)
./smoke-artifacts.sh --artifacts /tmp/out
./tools/test-tooling.sh              # build-artifacts.sh path/cleanup regression tests
```

Every workload is exercised through its **real entrypoint**: the extracted artifact is
mounted read-only at `/var/task` inside the AWS Lambda Node.js 24 base image
(`public.ecr.aws/lambda/nodejs:24`, `linux/arm64`) and `node /var/task/index.js` is run. For
the .NET workloads that means the Node.js `execve` shim actually replaces Node with the
self-contained apphost, so the deployed runtime contract — not a direct launch of the
executable — is what gets tested.

Without AWS, the smoke suite verifies:

- every `apps/*/index.js` entry point parses (`node --check`);
- static artifact checks: required entries, the `server` executable bit, and (Next.js) the
  presence of `linux-arm64` native modules with no host-only modules;
- **nextjs** — the SSR heading renders, the page reflects the artifact's `build-id.txt`, the
  `public/` asset is served, and a real `/_next/static/*.js` chunk returns HTTP 200;
- **aspnetcore** — `/health` reports the artifact's build id, `/status` returns a
  dynamically generated response, and the readiness POST reaches the mock runtime API;
- **blazor** — the rendered page, `/app.css`, and `/_framework/blazor.web.js` are served,
  and the readiness POST reaches the mock runtime API;
- the v1 and v2 archives differ by exactly the baked build id (`<id>` vs `<id>-v2`).

Containers are started without `--rm` so their logs survive a failure; the smoke script
prints the container and mock-runtime logs before failing, then removes the containers on
exit.

## Environment-specific limitations

- Docker is required for the smoke tests. If it is unavailable the container-backed checks
  are reported as **SKIPPED** (never as passed) and only the static artifact checks run.
- On Docker Desktop for macOS the build/smoke working directories live under the artifact
  directory (`build/.smoke/`) rather than the OS temp directory, because Docker Desktop does
  not share the macOS `TMPDIR` under `/var/folders`.
- Running `linux/arm64` images on an `x86_64` Linux host requires QEMU/binfmt emulation
  (e.g. `docker run --privileged --rm tonistiigi/binfmt --install arm64`). Without it, pull
  or exec of the base image fails; the smoke script does not install it automatically.
- Local smoke tests exercise the HTTP contract, the Node.js entrypoint shim, and the
  readiness handshake. Actual `AWS::Lambda::WebFunction` deployment, CloudFront/OAC
  behavior, image-optimizer behavior, and runtime scaling cannot be verified without AWS and
  are covered by the deployed validation in T7b.
- Blazor uses Static SSR only. Interactive Server / Auto are unsupported by the runtime
  (SignalR WebSocket is rejected), so no interactive render mode is configured.

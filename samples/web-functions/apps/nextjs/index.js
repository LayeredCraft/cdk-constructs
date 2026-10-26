// Lambda Web Function entry point for the Next.js standalone server.
//
// The managed runtime boots Node.js and imports this file as `index.js`. Node's HTTP
// server wrapper reads its listen address from HOSTNAME/PORT, while Lambda exposes the
// address the function must bind to as AWS_LAMBDA_HTTP_ENDPOINT ("host:port"). This shim
// bridges the two, then hands control to the generated standalone server.
const path = require("path");

const endpoint = process.env.AWS_LAMBDA_HTTP_ENDPOINT;
if (endpoint) {
  const separator = endpoint.lastIndexOf(":");
  if (separator > 0) {
    process.env.HOSTNAME = endpoint.slice(0, separator);
    process.env.PORT = endpoint.slice(separator + 1);
  }
}

// The artifact build writes build-id.txt next to this file; surface it to the app so a
// revision update can be observed from the rendered page.
if (!process.env.BUILD_ID) {
  try {
    process.env.BUILD_ID = require("fs")
      .readFileSync(path.join(__dirname, "build-id.txt"), "utf8")
      .trim();
  } catch {
    // build-id.txt is optional; the page falls back to "dev".
  }
}

// pnpm installs packages behind symlinks. Making the local node_modules discoverable via
// NODE_PATH keeps `require` resolution working regardless of the package manager used.
process.env.NODE_PATH = [process.env.NODE_PATH, path.join(__dirname, "node_modules")]
  .filter(Boolean)
  .join(path.delimiter);
require("module").Module._initPaths();

// Lambda sends SIGTERM before freezing the environment; exit cleanly so the shutdown
// fits inside the one-second grace window.
process.on("SIGTERM", () => process.exit(0));

require("./server.js");

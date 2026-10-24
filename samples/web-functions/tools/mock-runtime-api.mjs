// Minimal stand-in for the Lambda runtime interface API used by local smoke tests.
//
// The execve-shimmed servers POST /2025-09-01/ready once they are listening. This mock
// accepts that call and exposes GET /__ready so the smoke script can assert the handshake
// actually happened. It listens on all interfaces so a container can reach it through
// host.docker.internal.
import http from "node:http";

const port = Number(process.argv[2] ?? 0);
let ready = false;

const server = http.createServer((req, res) => {
  if (req.method === "POST" && req.url === "/2025-09-01/ready") {
    ready = true;
    req.resume();
    res.writeHead(200, { "content-type": "application/json" });
    res.end("{}");
    return;
  }

  if (req.method === "GET" && req.url === "/__ready") {
    res.writeHead(ready ? 200 : 404);
    res.end(ready ? "ready" : "not-ready");
    return;
  }

  res.writeHead(404);
  res.end();
});

server.listen(port, "0.0.0.0", () => {
  process.stdout.write(`${server.address().port}\n`);
});

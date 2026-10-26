// Lambda boots Node.js and imports this file as `index.js`. process.execve replaces the
// entire process image with the .NET server sitting next to it, so the revision's declared
// nodejs24.x runtime only ever runs this shim. The server then owns the readiness
// handshake (see Program.cs).
process.execve("/var/task/server", ["/var/task/server"], process.env);

import path from "node:path";
import { fileURLToPath } from "node:url";

const projectDir = path.dirname(fileURLToPath(import.meta.url));

/** @type {import('next').NextConfig} */
const nextConfig = {
  // Emit a self-contained server bundle under .next/standalone so the revision does not
  // need the full node_modules tree at runtime. This is the layout the Web Function
  // artifact recipe expects (standalone output + .next/static + public at the zip root).
  output: "standalone",
  // Pin the tracing root to this app so a lockfile in a parent directory cannot move the
  // standalone server: server.js must stay at the artifact root.
  outputFileTracingRoot: projectDir,
  // The sample does not use the Next.js image optimizer. Disabling it keeps the revision
  // from depending on the platform-specific sharp/libvips image pipeline.
  images: { unoptimized: true },
};

export default nextConfig;

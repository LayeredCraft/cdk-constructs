import { headers } from "next/headers";

// Render on every request so the page proves the server executes at request time rather
// than being served from a prebuilt static cache.
export const dynamic = "force-dynamic";

export default async function Home() {
  const requestHeaders = await headers();
  const requestId = requestHeaders.get("x-amzn-trace-id") ?? "local";
  const renderedAt = new Date().toISOString();
  const buildId = process.env.BUILD_ID ?? "dev";

  return (
    <main>
      <h1>Next.js Web Function Sample</h1>
      <p id="build-id">Build id: {buildId}</p>
      <p id="rendered-at">Rendered at {renderedAt}</p>
      <p id="request-id">Request id: {requestId}</p>
      <p>
        Static asset: <a href="/sample.txt">/sample.txt</a>
      </p>
    </main>
  );
}

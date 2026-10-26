export const metadata = {
  title: "Next.js Web Function Sample",
};

export default function RootLayout({ children }) {
  return (
    <html lang="en">
      <body>{children}</body>
    </html>
  );
}

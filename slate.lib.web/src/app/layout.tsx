import type { Metadata, Viewport } from "next";
import { Providers } from "@/components/layout/providers";
import "./globals.css";
export const metadata: Metadata = {
  title: "Slate — Your knowledge library",
  description: "A private space for everything you know.",
  applicationName: "slate.lib.web",
};
export const viewport: Viewport = {
  width: "device-width",
  initialScale: 1,
  viewportFit: "cover",
  themeColor: [
    { media: "(prefers-color-scheme: light)", color: "#fafbfc" },
    { media: "(prefers-color-scheme: dark)", color: "#20212a" },
  ],
};
export default function Layout({ children }: { children: React.ReactNode }) {
  return (
    <html lang="en" suppressHydrationWarning>
      <body>
        <Providers>{children}</Providers>
      </body>
    </html>
  );
}

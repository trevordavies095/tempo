import type { Metadata } from "next";
import { Geist, Geist_Mono } from "next/font/google";
import "./globals.css";
import { Providers } from "./providers";
import { Navbar } from "@/components/Navbar";
import { FaviconSync } from "@/components/FaviconSync";
import { AuthProvider } from "@/contexts/AuthContext";
import { APPEARANCE_BOOTSTRAP_SCRIPT } from "@/lib/appearance";

const geistSans = Geist({
  variable: "--font-geist-sans",
  subsets: ["latin"],
});

const geistMono = Geist_Mono({
  variable: "--font-geist-mono",
  subsets: ["latin"],
});

export const metadata: Metadata = {
  title: "Tempo - Running Tracker",
  description: "Self-hostable running tracker for privacy-conscious runners",
  icons: {
    icon: [
      {
        url: "/tempo-mark-ink.png",
        type: "image/png",
        media: "(prefers-color-scheme: light)",
      },
      {
        url: "/tempo-mark-volt.png",
        type: "image/png",
        media: "(prefers-color-scheme: dark)",
      },
    ],
    apple: [{ url: "/tempo-mark-volt.png", type: "image/png" }],
  },
};

export default function RootLayout({
  children,
}: Readonly<{
  children: React.ReactNode;
}>) {
  return (
    <html lang="en" suppressHydrationWarning>
      <head>
        <script
          dangerouslySetInnerHTML={{ __html: APPEARANCE_BOOTSTRAP_SCRIPT }}
        />
      </head>
      <body
        className={`${geistSans.variable} ${geistMono.variable} font-sans bg-canvas text-ink antialiased`}
      >
        <Providers>
          <FaviconSync />
          <AuthProvider>
            <Navbar />
            {children}
          </AuthProvider>
        </Providers>
      </body>
    </html>
  );
}

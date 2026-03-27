// !!---------------------------------------------------!!
// !!---------- AUTO-GENERATED: Do not edit! -----------!!
// !!---------------------------------------------------!!

import type { Metadata } from "next"
import { Poppins } from "next/font/google"

import "./globals.css"
import { AuthProvider } from "@/contexts/general/AuthContext"
import { HomeProvider } from "@/contexts/general/HomeContext"
import { ToastProvider } from "@/contexts/general/ToastContext"
import { LoadingProvider } from "@/contexts/general/LoadingContext"
import { DocumentProvider } from "@/contexts/general/DocumentContext"
import { LegacyModalProvider } from "@/contexts/legacy/ModalContext"
// [[PROVIDER_IMPORTS]]

const poppins = Poppins({
  variable: "--font-poppins",
  weight: ["400", "500", "600", "700"],
  subsets: ["latin"],
})

export const metadata: Metadata = {
  title: "SSETA Management Portal",
  description: "Management portal for SSETA projects and stakeholder information systems.",
}

export default function RootLayout({
  children,
}: Readonly<{
  children: React.ReactNode
}>) {
  return (
    <html lang="en">
      <body
        suppressHydrationWarning
        className={`${poppins.variable} antialiased`}
      >
        <LoadingProvider>
          <DocumentProvider>
            <LegacyModalProvider>
              <AuthProvider>
                <HomeProvider>
                  <ToastProvider>
                    {/* [[PROVIDERS]] */}
                  </ToastProvider>
                </HomeProvider>
              </AuthProvider>
            </LegacyModalProvider>
          </DocumentProvider>
        </LoadingProvider>
      </body>
    </html>
  )
}

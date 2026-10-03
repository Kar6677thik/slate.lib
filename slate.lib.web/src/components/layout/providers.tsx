"use client";
import { useState, type ReactNode } from "react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { ThemeProvider } from "next-themes";
import * as Tooltip from "@radix-ui/react-tooltip";
import { PreferencesProvider } from "@/lib/storage/preferences";
import { Pwa } from "./pwa";
import { AuthProvider } from "@/lib/auth/context";
export function Providers({ children }: { children: ReactNode }) {
  const [client] = useState(
    () =>
      new QueryClient({
        defaultOptions: {
          queries: { staleTime: 30000, retry: 1, refetchOnWindowFocus: false },
          mutations: { retry: false },
        },
      }),
  );
  return (
    <ThemeProvider attribute="class" defaultTheme="light" enableSystem>
      <QueryClientProvider client={client}>
        <Tooltip.Provider delayDuration={350}>
          <AuthProvider>
            <PreferencesProvider>
              <Pwa />
              {children}
            </PreferencesProvider>
          </AuthProvider>
        </Tooltip.Provider>
      </QueryClientProvider>
    </ThemeProvider>
  );
}

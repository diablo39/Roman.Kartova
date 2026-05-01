"use client"

import {
  CheckCircle,
  InfoCircle,
  Loading01,
  AlertOctagon,
  AlertTriangle,
} from "@untitledui/icons"
import { useTheme } from "next-themes"
import { Toaster as Sonner, type ToasterProps } from "sonner"

const Toaster = ({ ...props }: ToasterProps) => {
  const { theme = "system" } = useTheme()

  return (
    <Sonner
      theme={theme as ToasterProps["theme"]}
      className="toaster group"
      icons={{
        success: <CheckCircle className="size-4" />,
        info: <InfoCircle className="size-4" />,
        warning: <AlertTriangle className="size-4" />,
        error: <AlertOctagon className="size-4" />,
        loading: <Loading01 className="size-4 animate-spin" />,
      }}
      style={
        {
          "--normal-bg": "var(--color-bg-primary)",
          "--normal-text": "var(--color-text-primary)",
          "--normal-border": "var(--color-border-primary)",
          "--border-radius": "0.5rem",
        } as React.CSSProperties
      }
      {...props}
    />
  )
}

export { Toaster }

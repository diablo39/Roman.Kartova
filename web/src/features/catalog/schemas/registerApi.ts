import { z } from "zod";
import type { components } from "@/generated/openapi";

/** Wire-shape style union, sourced from the OpenAPI codegen (single source of truth). */
type ApiStyleValue = components["schemas"]["ApiResponse"]["style"];

/** Style values in wire form (camelCase per JsonStringEnumConverter + JsonNamingPolicy.CamelCase).
 *  The `satisfies` clause fails the build if any literal's casing drifts from the generated client. */
export const API_STYLES = ["rest", "grpc", "graphQL"] as const satisfies readonly ApiStyleValue[];

/** Human-friendly labels for the style <select>, table badge, and detail page.
 *  Typed as a total Record so a missing/extra key fails `tsc`. */
export const API_STYLE_LABEL: Record<ApiStyleValue, string> = {
  rest: "REST",
  grpc: "gRPC",
  graphQL: "GraphQL",
};

function isAbsoluteUrl(value: string): boolean {
  try {
    const u = new URL(value);
    return !!u.protocol && !!u.host;
  } catch {
    return false;
  }
}

export const registerApiSchema = z.object({
  displayName: z.string().min(1, "Display Name must not be empty").max(128, "Display Name must be at most 128 characters"),
  description: z.string().min(1, "Description is required").max(4096, "Description must be at most 4096 characters"),
  style: z.enum(API_STYLES),
  version: z.string().min(1, "Version must not be empty").max(64, "Version must be at most 64 characters"),
  // Optional: empty string ⇒ omitted; when present must be an absolute URL (mirrors Api.Create ValidateSpecUrl).
  specUrl: z
    .string()
    .max(2048, "Spec URL must be at most 2048 characters")
    .refine((v) => v === "" || isAbsoluteUrl(v), "Spec URL must be an absolute URL (include a scheme and host)")
    .optional()
    .or(z.literal("")),
  teamId: z.string().uuid("Team is required"),
});

export type RegisterApiInput = z.infer<typeof registerApiSchema>;
export type { ApiStyleValue };

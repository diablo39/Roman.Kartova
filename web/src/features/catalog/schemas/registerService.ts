import { z } from "zod";
import type { components } from "@/generated/openapi";

/** Wire-shape protocol union, sourced from the OpenAPI codegen (single source of truth). */
type ProtocolValue = components["schemas"]["ServiceEndpointDto"]["protocol"];

/**
 * Protocol values in wire form (camelCase per JsonStringEnumConverter +
 * JsonNamingPolicy.CamelCase). The `satisfies readonly ProtocolValue[]` clause
 * fails the build if any literal's casing drifts from the generated client.
 */
export const PROTOCOLS = ["rest", "grpc", "graphQL", "webSocket", "tcp", "other"] as const satisfies readonly ProtocolValue[];

/** Human-friendly labels for the protocol <select> and the detail table.
 *  Typed as a total Record so a missing/extra key fails `tsc`. */
export const PROTOCOL_LABEL: Record<ProtocolValue, string> = {
  rest: "REST",
  grpc: "gRPC",
  graphQL: "GraphQL",
  webSocket: "WebSocket",
  tcp: "TCP",
  other: "Other",
};

function isAbsoluteUrl(value: string): boolean {
  try {
    const u = new URL(value);
    return !!u.protocol && !!u.host;
  } catch {
    return false;
  }
}

export const endpointSchema = z.object({
  url: z
    .string()
    .min(1, "Endpoint URL must not be empty")
    .max(2048, "Endpoint URL must be at most 2048 characters")
    .refine(isAbsoluteUrl, "Endpoint URL must be an absolute URL (include a scheme and host)"),
  protocol: z.enum(PROTOCOLS),
});

export const registerServiceSchema = z.object({
  displayName: z.string().min(1, "Display Name must not be empty").max(128, "Display Name must be at most 128 characters"),
  description: z.string().min(1, "Description is required").max(4096, "Description must be at most 4096 characters"),
  teamId: z
    .string()
    .regex(
      /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i,
      "Team is required",
    ),
  endpoints: z.array(endpointSchema).max(50, "A service may have at most 50 endpoints"),
});

export type RegisterServiceInput = z.infer<typeof registerServiceSchema>;
export type EndpointInput = z.infer<typeof endpointSchema>;
export type { ProtocolValue };

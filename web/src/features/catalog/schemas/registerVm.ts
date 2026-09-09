import { z } from "zod";
import type { PowerState } from "@/features/catalog/powerState";

/**
 * Power-state values in wire form. `VmAttributesDto.powerState` is generated as a bare
 * `string` (the backend enum isn't reflected in the OpenAPI schema — see the comment on
 * `PowerState` in `powerState.ts`), so there is no generated union to check literals
 * against directly. Instead this is tied to that hand-mirrored `PowerState` type: the
 * `satisfies readonly PowerState[]` clause still fails the build if a literal here drifts
 * from the one badges/tables use.
 */
export const POWER_STATES = ["running", "stopped", "suspended"] as const satisfies readonly PowerState[];

function isValidIpAddress(value: string): boolean {
  return z.ipv4().safeParse(value).success || z.ipv6().safeParse(value).success;
}

/**
 * vCPU/memory are validated (and kept) as digit strings rather than coerced to `number`.
 * `VmAttributesDto.vcpu`/`memoryGb` are generated as `number | string` (openapi-typescript's
 * int32-as-string handling), so a validated string is already wire-valid — no coercion is
 * needed. This also keeps the form's values type identical to the schema's parsed-output
 * type (no z.input/z.output split), matching every other field here and the simpler
 * `registerServiceSchema` pattern this mirrors.
 */
function positiveIntStringSchema(label: string) {
  return z
    .string()
    .min(1, `${label} is required`)
    .regex(/^[1-9]\d*$/, `${label} must be a positive whole number`);
}

export const vmAttributesSchema = z.object({
  powerState: z.enum(POWER_STATES),
  os: z.string().min(1, "OS must not be empty").max(128, "OS must be at most 128 characters"),
  hostname: z.string().min(1, "Hostname must not be empty").max(255, "Hostname must be at most 255 characters"),
  region: z.string().min(1, "Region must not be empty").max(128, "Region must be at most 128 characters"),
  vcpu: positiveIntStringSchema("vCPU"),
  memoryGb: positiveIntStringSchema("Memory"),
  // Validated as a single array-level refine (rather than per-element via z.array(ipSchema))
  // so a bad entry surfaces one message at the `attributes.ipAddresses` path — the exact path
  // `FormField`/`fieldState.error` reads for the InputTags field, matching how every other
  // field in this schema reports its error.
  ipAddresses: z
    .array(z.string().min(1, "IP address must not be empty"))
    .min(1, "At least one IP address is required")
    .refine((ips) => ips.every(isValidIpAddress), "Each IP address must be a valid IPv4 or IPv6 address"),
});

export const registerVmSchema = z.object({
  displayName: z.string().min(1, "Display Name must not be empty").max(128, "Display Name must be at most 128 characters"),
  description: z.string().min(1, "Description is required").max(4096, "Description must be at most 4096 characters"),
  teamId: z.string().uuid("Team is required"),
  provider: z.string().max(256, "Provider must be at most 256 characters").optional(),
  attributes: vmAttributesSchema,
});

export type RegisterVmInput = z.infer<typeof registerVmSchema>;

// Edit does not move team — T5 keeps team immutable (only PUT .../vms/{id}/team, not
// modeled yet, could reassign it). Mirrors editApplicationSchema's narrower shape
// relative to registration.
export const editVmSchema = registerVmSchema.omit({ teamId: true });
export type EditVmInput = z.infer<typeof editVmSchema>;

export type VmAttributesInput = z.infer<typeof vmAttributesSchema>;
export type { PowerState };

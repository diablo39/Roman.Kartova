import type { ReactNode } from "react";
import type { Control, FieldPath } from "react-hook-form";

import { FormField } from "@/components/base/form/hook-form";
import { Input } from "@/components/base/input/input";
import { InputTags } from "@/components/base/input/input-tags";
import { TextArea } from "@/components/base/textarea/textarea";

import { POWER_STATES, type RegisterVmInput } from "@/features/catalog/schemas/registerVm";
import { powerStateLabel } from "@/features/catalog/powerState";

/**
 * Fields this component reads, shared structurally by both `RegisterVmInput` and
 * `EditVmInput` (`editVmSchema = registerVmSchema.omit({ teamId: true })`, so every
 * field here exists on both with the same type). `VmFormFields` is generic over the
 * caller's own field-values type (constrained to this shape) rather than narrowing/
 * widening a `Control<...>` with a cast at either call site — see gate-7 M1.
 */
type VmSharedFormFields = Pick<RegisterVmInput, "displayName" | "description" | "provider" | "attributes">;

interface Props<TFieldValues extends VmSharedFormFields> {
  control: Control<TFieldValues>;
  /** Prefixes each field's `id`/`data-testid` so the two dialogs don't collide when both
   * could theoretically be mounted (mirrors the previous `register-vm-*` / `edit-vm-*`
   * naming). */
  idPrefix: string;
  /** Mirrors each dialog's pre-existing `mutation.isPending` wiring, which only ever
   * covered the power-state `<select>` and the IP-addresses `InputTags` — the plain
   * text/number inputs here (displayName, description, provider, os, hostname, region,
   * vcpu, memoryGb) stay enabled during submit; that is unchanged pre-existing behavior,
   * not something this prop derives from RHF. */
  disabled?: boolean;
  /** Renders between `provider` and `attributes.powerState` — preserves `RegisterVmDialog`'s
   * exact original field order for its `teamId` select (immutable on edit, so
   * `EditVmDialog` leaves this unset). */
  afterProvider?: ReactNode;
}

/**
 * Shared VM form fields — extracted from `RegisterVmDialog` + `EditVmDialog` (/simplify
 * cleanup, slice 2a): displayName, description, provider, power state, the
 * os/hostname/region/vcpu/memoryGb grid, and IP addresses. `RegisterVmDialog` wraps this
 * with its own `teamId` field and "Created by" block; `EditVmDialog` uses it standalone
 * (team is immutable on edit). Each dialog keeps its own submit/error-handling wiring.
 */
export function VmFormFields<TFieldValues extends VmSharedFormFields>({
  control,
  idPrefix,
  disabled,
  afterProvider,
}: Props<TFieldValues>) {
  // Each literal below is a real path on VmSharedFormFields — TFieldValues extends that
  // shape, so every one of these paths is guaranteed to exist on TFieldValues too. The
  // intersection cast (rather than widening to the whole FieldPath<TFieldValues> union)
  // keeps the literal type FormField needs to compute this field's precise value type —
  // widening it to the union is what previously made every field's `value` a union of
  // every field's value type (string | string[] | the whole attributes object), breaking
  // every typed input prop. This is a narrow, per-literal assertion restating what the
  // generic constraint already guarantees — not the Control<A> ↔ Control<B> cast (gate-7
  // M1) that defeated the structural check between the two concrete form schemas.
  const name = <TName extends FieldPath<VmSharedFormFields>>(path: TName) =>
    path as unknown as TName & FieldPath<TFieldValues>;

  return (
    <>
      <FormField name={name("displayName")} control={control}>
        {({ field, fieldState }) => (
          <Input
            label="Display Name"
            placeholder="web-prod-01"
            hint={fieldState.error?.message ?? "Human-friendly name shown in UI."}
            isInvalid={!!fieldState.error}
            isRequired
            {...field}
          />
        )}
      </FormField>
      <FormField name={name("description")} control={control}>
        {({ field, fieldState }) => (
          <TextArea
            label="Description"
            rows={3}
            placeholder="Short summary..."
            hint={fieldState.error?.message}
            isInvalid={!!fieldState.error}
            isRequired
            {...field}
          />
        )}
      </FormField>

      <FormField name={name("provider")} control={control}>
        {({ field, fieldState }) => (
          <Input
            label="Provider"
            placeholder="AWS, Azure, on-prem…"
            hint={fieldState.error?.message ?? "Optional — the cloud or hosting provider."}
            isInvalid={!!fieldState.error}
            {...field}
            value={field.value ?? ""}
          />
        )}
      </FormField>

      {afterProvider}

      <FormField name={name("attributes.powerState")} control={control}>
        {({ field, fieldState }) => (
          <div className="flex flex-col gap-1">
            <label htmlFor={`${idPrefix}-power-state`} className="text-sm font-medium text-secondary">
              Power State <span className="text-error-primary">*</span>
            </label>
            <select
              id={`${idPrefix}-power-state`}
              data-testid={`${idPrefix}-power-state-select`}
              className="rounded-md border border-secondary px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-brand-500 disabled:opacity-60 bg-primary text-primary"
              value={field.value}
              onChange={field.onChange}
              onBlur={field.onBlur}
              ref={field.ref}
              disabled={disabled}
              aria-invalid={!!fieldState.error}
            >
              {POWER_STATES.map((state) => (
                <option key={state} value={state}>{powerStateLabel(state)}</option>
              ))}
            </select>
            {fieldState.error && <p className="text-xs text-error-primary">{fieldState.error.message}</p>}
          </div>
        )}
      </FormField>

      <div className="grid grid-cols-1 gap-5 sm:grid-cols-2">
        <FormField name={name("attributes.os")} control={control}>
          {({ field, fieldState }) => (
            <Input
              label="OS"
              placeholder="Ubuntu 24.04"
              hint={fieldState.error?.message}
              isInvalid={!!fieldState.error}
              isRequired
              {...field}
            />
          )}
        </FormField>
        <FormField name={name("attributes.hostname")} control={control}>
          {({ field, fieldState }) => (
            <Input
              label="Hostname"
              placeholder="web-prod-01.internal"
              hint={fieldState.error?.message}
              isInvalid={!!fieldState.error}
              isRequired
              {...field}
            />
          )}
        </FormField>
        <FormField name={name("attributes.region")} control={control}>
          {({ field, fieldState }) => (
            <Input
              label="Region"
              placeholder="eu-west-1"
              hint={fieldState.error?.message}
              isInvalid={!!fieldState.error}
              isRequired
              {...field}
            />
          )}
        </FormField>
        <FormField name={name("attributes.vcpu")} control={control}>
          {({ field, fieldState }) => (
            <Input
              label="vCPU"
              type="number"
              placeholder="2"
              hint={fieldState.error?.message}
              isInvalid={!!fieldState.error}
              isRequired
              {...field}
            />
          )}
        </FormField>
        <FormField name={name("attributes.memoryGb")} control={control}>
          {({ field, fieldState }) => (
            <Input
              label="Memory (GB)"
              type="number"
              placeholder="4"
              hint={fieldState.error?.message}
              isInvalid={!!fieldState.error}
              isRequired
              {...field}
            />
          )}
        </FormField>
      </div>

      <FormField name={name("attributes.ipAddresses")} control={control}>
        {({ field, fieldState }) => (
          <InputTags
            label="IP Addresses"
            placeholder="10.0.0.5 — press Enter to add"
            hint={fieldState.error?.message ?? "Press Enter after each address. At least one is required."}
            isInvalid={!!fieldState.error}
            isRequired
            isDisabled={disabled}
            value={field.value}
            onChange={field.onChange}
          />
        )}
      </FormField>
    </>
  );
}

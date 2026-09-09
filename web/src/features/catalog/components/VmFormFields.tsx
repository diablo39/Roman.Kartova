import type { ReactNode } from "react";
import type { Control } from "react-hook-form";

import { FormField } from "@/components/base/form/hook-form";
import { Input } from "@/components/base/input/input";
import { InputTags } from "@/components/base/input/input-tags";
import { TextArea } from "@/components/base/textarea/textarea";

import { POWER_STATES, type EditVmInput } from "@/features/catalog/schemas/registerVm";
import { powerStateLabel } from "@/features/catalog/powerState";

interface Props {
  /**
   * Typed against `EditVmInput` (the narrower, shared shape — `RegisterVmInput` minus
   * `teamId`). `RegisterVmDialog`'s `Control<RegisterVmInput>` is a structural superset —
   * every path this component reads (`displayName`/`description`/`provider`/`attributes.*`)
   * exists on both — so it is passed in with a narrowing cast at that call site.
   */
  control: Control<EditVmInput>;
  /** Prefixes each field's `id`/`data-testid` so the two dialogs don't collide when both
   * could theoretically be mounted (mirrors the previous `register-vm-*` / `edit-vm-*`
   * naming). */
  idPrefix: string;
  /** Disables the controls that don't already derive their disabled state from RHF
   * (mirrors each dialog's `mutation.isPending` wiring for the power-state select and the
   * IP-addresses tag input). */
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
export function VmFormFields({ control, idPrefix, disabled, afterProvider }: Props) {
  return (
    <>
      <FormField name="displayName" control={control}>
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
      <FormField name="description" control={control}>
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

      <FormField name="provider" control={control}>
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

      <FormField name="attributes.powerState" control={control}>
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
        <FormField name="attributes.os" control={control}>
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
        <FormField name="attributes.hostname" control={control}>
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
        <FormField name="attributes.region" control={control}>
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
        <FormField name="attributes.vcpu" control={control}>
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
        <FormField name="attributes.memoryGb" control={control}>
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

      <FormField name="attributes.ipAddresses" control={control}>
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

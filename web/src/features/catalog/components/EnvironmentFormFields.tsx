import type { ReactNode } from "react";
import type { Control, FieldPath } from "react-hook-form";

import { FormField } from "@/components/base/form/hook-form";
import { Input } from "@/components/base/input/input";
import { TextArea } from "@/components/base/textarea/textarea";

import type { RegisterEnvironmentForm } from "@/features/catalog/schemas/registerEnvironment";

/**
 * Fields this component reads, shared structurally by both `RegisterEnvironmentForm` and
 * `EditEnvironmentForm` (`editEnvironmentSchema = registerEnvironmentSchema.omit({ type: true })`
 * — `type` is immutable on edit, mirroring `VmFormFields`' `teamId` omission). Generic over
 * the caller's own field-values type (gate-7 M1).
 */
type EnvironmentSharedFormFields = Pick<RegisterEnvironmentForm, "displayName" | "description" | "region">;

interface Props<TFieldValues extends EnvironmentSharedFormFields> {
  control: Control<TFieldValues>;
  /** Renders before `region`, in the same 2-col grid — `RegisterEnvironmentDialog` supplies
   * the `type` `<select>` here (mutable only at register time, so its `disabled`/`idPrefix`
   * wiring lives with that caller); `EditEnvironmentDialog` leaves this unset since `type` is
   * immutable on edit. Mirrors `VmFormFields`' `afterProvider` slot for the same
   * immutable-field-exclusion shape. */
  beforeRegion?: ReactNode;
}

/** Shared Environment form fields — extracted from `RegisterEnvironmentDialog` for reuse by
 * `EditEnvironmentDialog` (A2), mirroring `VmFormFields`. */
export function EnvironmentFormFields<TFieldValues extends EnvironmentSharedFormFields>({
  control,
  beforeRegion,
}: Props<TFieldValues>) {
  const name = <TName extends FieldPath<EnvironmentSharedFormFields>>(path: TName) =>
    path as unknown as TName & FieldPath<TFieldValues>;

  return (
    <>
      <FormField name={name("displayName")} control={control}>
        {({ field, fieldState }) => (
          <Input
            label="Display Name"
            placeholder="Production"
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

      <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
        {beforeRegion}
        <FormField name={name("region")} control={control}>
          {({ field, fieldState }) => (
            <Input
              label="Region"
              placeholder="eu-west-1"
              hint={fieldState.error?.message}
              isInvalid={!!fieldState.error}
              {...field}
            />
          )}
        </FormField>
      </div>
    </>
  );
}

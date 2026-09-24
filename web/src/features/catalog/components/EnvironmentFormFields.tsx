import type { Control, FieldPath } from "react-hook-form";

import { FormField } from "@/components/base/form/hook-form";
import { Input } from "@/components/base/input/input";
import { TextArea } from "@/components/base/textarea/textarea";

import { environmentTypes, type RegisterEnvironmentForm } from "@/features/catalog/schemas/registerEnvironment";
import { environmentTypeLabel } from "@/features/catalog/components/EnvironmentTable";

/**
 * Fields this component reads, shared structurally by both `RegisterEnvironmentForm` and
 * `EditEnvironmentForm` (`editEnvironmentSchema = registerEnvironmentSchema` — no field is
 * immutable, unlike VmFormFields' teamId omission). Generic over the caller's own
 * field-values type, mirroring VmFormFields (gate-7 M1).
 */
type EnvironmentSharedFormFields = Pick<RegisterEnvironmentForm, "displayName" | "description" | "type" | "region">;

interface Props<TFieldValues extends EnvironmentSharedFormFields> {
  control: Control<TFieldValues>;
  idPrefix: string;
  disabled?: boolean;
}

/** Shared Environment form fields — extracted from `RegisterEnvironmentDialog` for reuse by
 * `EditEnvironmentDialog` (A2), mirroring `VmFormFields`. */
export function EnvironmentFormFields<TFieldValues extends EnvironmentSharedFormFields>({
  control,
  idPrefix,
  disabled,
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
        <FormField name={name("type")} control={control}>
          {({ field }) => (
            <div className="flex flex-col gap-1">
              <label htmlFor={`${idPrefix}-type`} className="text-sm font-medium text-secondary">
                Type
              </label>
              <select
                id={`${idPrefix}-type`}
                data-testid={`${idPrefix}-type-select`}
                className="rounded-md border border-secondary px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-brand-500 disabled:opacity-60 bg-primary text-primary"
                value={field.value}
                onChange={field.onChange}
                onBlur={field.onBlur}
                ref={field.ref}
                disabled={disabled}
              >
                {environmentTypes.map((t) => (
                  <option key={t} value={t}>{environmentTypeLabel(t)}</option>
                ))}
              </select>
            </div>
          )}
        </FormField>
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

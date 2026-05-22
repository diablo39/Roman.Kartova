import { useState } from "react";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { toast } from "sonner";

import { ModalOverlay, Modal, Dialog } from "@/components/application/modals/modal";
import { HookForm, FormField } from "@/components/base/form/hook-form";
import { Input } from "@/components/base/input/input";
import { Button } from "@/components/base/buttons/button";

import {
  unDecommissionApplicationSchema,
  type UnDecommissionApplicationInput,
} from "@/features/catalog/schemas/unDecommissionApplication";
import {
  useUnDecommissionApplication,
  type ApplicationResponse,
} from "@/features/catalog/api/applications";
import { isLifecycle, lifecycleLabel } from "@/features/catalog/lifecycle";
import {
  applyProblemDetailsToForm,
  type ProblemDetails,
} from "@/shared/forms/problemDetails";
import {
  isoDateAtMidnight,
  toDateInputValue,
  fromDateInputValue,
} from "@/features/catalog/dateUtils";

interface Props {
  application: ApplicationResponse;
  open: boolean;
  onOpenChange: (open: boolean) => void;
}

/**
 * Un-decommission confirmation modal — reverse-lifecycle action (OrgAdmin only).
 *
 * Transitions: Decommissioned → Deprecated. Requires a new future sunset date.
 *
 * Server-error UX:
 *  - 400 ProblemDetails with `errors` map → field errors via
 *    `applyProblemDetailsToForm`; dialog stays open.
 *  - 409 LifecycleConflict → toast with the server's reported
 *    `currentLifecycle`, then close.
 *  - Anything else → fallback toast; dialog stays open for retry.
 */
export function UnDecommissionConfirmDialog({ application, open, onOpenChange }: Props) {
  const mutation = useUnDecommissionApplication(application.id);

  // Lazy `useState` keeps the impure `Date.now()` read off the render path.
  const [initialSunset] = useState(() => isoDateAtMidnight(Date.now(), 30));

  const form = useForm<UnDecommissionApplicationInput>({
    resolver: zodResolver(unDecommissionApplicationSchema),
    defaultValues: { sunsetDate: initialSunset },
  });

  const onSubmit = form.handleSubmit(async (values) => {
    try {
      await mutation.mutateAsync(values);
      const dateLabel = new Date(values.sunsetDate).toLocaleDateString();
      toast.success(`${application.displayName} restored to Deprecated. Sunset on ${dateLabel}.`);
      onOpenChange(false);
    } catch (err) {
      const problem = err as ProblemDetails & {
        __status?: number;
        currentLifecycle?: string;
      };
      const status = problem.__status;

      const handled = applyProblemDetailsToForm(problem, (name, error) =>
        form.setError(name as Parameters<typeof form.setError>[0], error)
      );
      if (handled) return;

      if (status === 409) {
        const current = isLifecycle(problem.currentLifecycle)
          ? lifecycleLabel(problem.currentLifecycle)
          : "an unexpected state";
        toast.error(`Cannot restore to deprecated — current state is ${current}.`);
        onOpenChange(false);
        return;
      }

      const detail = problem.detail ?? problem.title ?? "Could not restore application to deprecated";
      toast.error(detail);
    }
  });

  return (
    <ModalOverlay isOpen={open} onOpenChange={onOpenChange} isDismissable={!mutation.isPending}>
      <Modal className="max-w-[480px]">
        <Dialog aria-label="Restore to Deprecated" className="bg-primary rounded-xl shadow-xl p-6 outline-none">
          <div className="space-y-1 mb-4">
            <h2 className="text-lg font-semibold text-primary">Restore {application.displayName} to Deprecated?</h2>
            <p className="text-sm text-tertiary">
              Restore <strong>{application.displayName}</strong> to <strong>Deprecated</strong>?{" "}
              Provide a new future sunset date.
            </p>
          </div>

          <HookForm form={form} onSubmit={onSubmit} className="space-y-5">
            <FormField name="sunsetDate" control={form.control}>
              {({ field, fieldState }) => (
                <Input
                  type="date"
                  label="Sunset date"
                  hint={fieldState.error?.message ?? "Must be in the future."}
                  isInvalid={!!fieldState.error}
                  isRequired
                  name={field.name}
                  value={toDateInputValue(field.value ?? "")}
                  onBlur={field.onBlur}
                  onChange={(v) => field.onChange(fromDateInputValue(v))}
                />
              )}
            </FormField>

            <div className="flex justify-end gap-2 pt-2">
              <Button type="button" color="secondary" size="sm" onClick={() => onOpenChange(false)}>
                Cancel
              </Button>
              <Button type="submit" color="primary" size="sm" isLoading={mutation.isPending}>
                Restore to Deprecated
              </Button>
            </div>
          </HookForm>
        </Dialog>
      </Modal>
    </ModalOverlay>
  );
}

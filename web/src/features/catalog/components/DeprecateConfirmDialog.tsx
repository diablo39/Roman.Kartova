import { useState } from "react";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { toast } from "sonner";

import { ModalOverlay, Modal, Dialog } from "@/components/application/modals/modal";
import { HookForm, FormField } from "@/components/base/form/hook-form";
import { Input } from "@/components/base/input/input";
import { Button } from "@/components/base/buttons/button";

import {
  deprecateApplicationSchema,
  type DeprecateApplicationInput,
} from "@/features/catalog/schemas/deprecateApplication";
import {
  useDeprecateApplication,
  type ApplicationResponse,
} from "@/features/catalog/api/applications";
import { isLifecycle, lifecycleLabel } from "@/features/catalog/lifecycle";
import {
  applyProblemDetailsToForm,
  type ProblemDetails,
} from "@/shared/forms/problemDetails";

interface Props {
  application: ApplicationResponse;
  open: boolean;
  onOpenChange: (open: boolean) => void;
}

const MS_PER_DAY = 24 * 60 * 60 * 1000;

/** YYYY-MM-DD string at UTC midnight `n` days from `now`. */
function isoDateAtMidnight(now: number, daysFromNow: number): string {
  const d = new Date(now + daysFromNow * MS_PER_DAY);
  d.setUTCHours(0, 0, 0, 0);
  return d.toISOString();
}

/** Strip time portion: ISO string → "YYYY-MM-DD" for native `<input type="date">`. */
function toDateInputValue(iso: string): string {
  if (!iso) return "";
  const idx = iso.indexOf("T");
  return idx >= 0 ? iso.slice(0, idx) : iso;
}

/** Inflate "YYYY-MM-DD" → UTC-midnight ISO string. */
function fromDateInputValue(local: string): string {
  if (!local) return "";
  // Treat as UTC midnight so the wire value is unambiguous.
  return new Date(`${local}T00:00:00Z`).toISOString();
}

/**
 * Deprecate confirmation modal — mirrors EditApplicationDialog shape.
 *
 * Pre-fills `sunsetDate` to today + 30 days at UTC midnight. The schema's
 * "must be in the future" `refine` is the authoritative client-side guard;
 * the project's Untitled UI Input wrapper does not forward `min` through to
 * the underlying date input, so a leading-zero past entry triggers a field
 * error at submit rather than being suppressed by browser UI.
 *
 * Server-error UX:
 *  - 400 ProblemDetails with `errors` map → field errors via
 *    `applyProblemDetailsToForm`; dialog stays open.
 *  - 409 LifecycleConflict → toast with the server's reported
 *    `currentLifecycle`, then close.
 *  - Anything else → fallback toast; dialog stays open for retry.
 */
export function DeprecateConfirmDialog({ application, open, onOpenChange }: Props) {
  const mutation = useDeprecateApplication(application.id);

  // Lazy `useState` keeps the impure `Date.now()` read off the render path
  // (react-hooks/purity). Parent remounts on open, so the seed stays current.
  const [initialSunset] = useState(() => isoDateAtMidnight(Date.now(), 30));

  const form = useForm<DeprecateApplicationInput>({
    resolver: zodResolver(deprecateApplicationSchema),
    defaultValues: { sunsetDate: initialSunset },
  });

  const onSubmit = form.handleSubmit(async (values) => {
    try {
      await mutation.mutateAsync(values);
      const dateLabel = new Date(values.sunsetDate).toLocaleDateString();
      toast.success(`Application deprecated. Sunset on ${dateLabel}.`);
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
        toast.error(`Cannot deprecate — current state is ${current}.`);
        onOpenChange(false);
        return;
      }

      const detail = problem.detail ?? problem.title ?? "Could not deprecate application";
      toast.error(detail);
    }
  });

  return (
    <ModalOverlay isOpen={open} onOpenChange={onOpenChange} isDismissable={!mutation.isPending}>
      <Modal className="max-w-[480px]">
        <Dialog aria-label="Deprecate Application" className="bg-primary rounded-xl shadow-xl p-6 outline-none">
          <div className="space-y-1 mb-4">
            <h2 className="text-lg font-semibold text-primary">Deprecate {application.displayName}</h2>
            <p className="text-sm text-tertiary">
              Mark this application as deprecated. After the sunset date, you'll be able to decommission it. The application will continue to function until then.
            </p>
          </div>

          <HookForm form={form} onSubmit={onSubmit} className="space-y-5">
            <FormField name="sunsetDate" control={form.control}>
              {({ field, fieldState }) => (
                <Input
                  type="date"
                  label="Sunset date"
                  hint={fieldState.error?.message ?? "Must be in the future. Decommission becomes available after this date."}
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
                Deprecate
              </Button>
            </div>
          </HookForm>
        </Dialog>
      </Modal>
    </ModalOverlay>
  );
}

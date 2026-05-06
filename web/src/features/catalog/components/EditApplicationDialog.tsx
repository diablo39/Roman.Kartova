import { useEffect } from "react";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { toast } from "sonner";

import { ModalOverlay, Modal, Dialog } from "@/components/application/modals/modal";
import { HookForm, FormField } from "@/components/base/form/hook-form";
import { Input } from "@/components/base/input/input";
import { TextArea } from "@/components/base/textarea/textarea";
import { Button } from "@/components/base/buttons/button";

import {
  editApplicationSchema,
  type EditApplicationInput,
} from "@/features/catalog/schemas/editApplication";
import {
  useEditApplication,
  type ApplicationResponse,
} from "@/features/catalog/api/applications";
import {
  applyProblemDetailsToForm,
  type ProblemDetails,
} from "@/shared/forms/problemDetails";

interface Props {
  application: ApplicationResponse;
  open: boolean;
  onOpenChange: (open: boolean) => void;
}

/**
 * Edit-application modal — mirrors RegisterApplicationDialog shape.
 *
 * Pre-fills `displayName` + `description` from the supplied application.
 * On submit, calls `useEditApplication` (which sends `If-Match: "<version>"`).
 *
 * Server-error UX (per spec §8.3):
 *  - 400 ProblemDetails with `errors` map → fields set via
 *    `applyProblemDetailsToForm`; dialog stays open.
 *  - 412 ConcurrencyConflict → toast + dialog stays open. The hook's
 *    `onSuccess` does not fire on 412, so the cached detail-query value
 *    is unchanged; the parent page is responsible for invalidating /
 *    refetching the detail query (and re-passing fresh `application`
 *    props), at which point `useEffect` resets the form.
 *  - 409 LifecycleConflict (Decommissioned) → toast + close.
 *  - Anything else → generic toast + dialog stays open for retry.
 */
export function EditApplicationDialog({ application, open, onOpenChange }: Props) {
  const mutation = useEditApplication(application.id);
  const form = useForm<EditApplicationInput>({
    resolver: zodResolver(editApplicationSchema),
    defaultValues: {
      displayName: application.displayName,
      description: application.description,
    },
  });

  // Re-sync defaults when the application prop changes (e.g. after 412 refetch
  // upstream) or when the dialog reopens, so the form always reflects the
  // server's current state.
  useEffect(() => {
    form.reset({
      displayName: application.displayName,
      description: application.description,
    });
  }, [application.displayName, application.description, open, form]);

  const onSubmit = form.handleSubmit(async (values) => {
    try {
      await mutation.mutateAsync({ values, expectedVersion: application.version });
      toast.success("Application updated");
      onOpenChange(false);
    } catch (err) {
      const problem = err as ProblemDetails & { __status?: number };
      const status = problem.__status;

      const handled = applyProblemDetailsToForm(problem, (name, error) =>
        form.setError(name as Parameters<typeof form.setError>[0], error)
      );
      if (handled) return; // 400 — field errors set, leave dialog open.

      if (status === 412) {
        toast.error("Someone else edited this. Reload to see the latest values.");
        return;
      }
      if (status === 409) {
        toast.error("This application has been decommissioned and can no longer be edited.");
        onOpenChange(false);
        return;
      }

      const detail = problem.detail ?? problem.title ?? "Could not update application";
      toast.error(detail);
    }
  });

  return (
    <ModalOverlay isOpen={open} onOpenChange={onOpenChange} isDismissable={!mutation.isPending}>
      <Modal className="max-w-[560px]">
        <Dialog aria-label="Edit Application" className="bg-primary rounded-xl shadow-xl p-6 outline-none">
          <div className="w-full">
            <div className="space-y-1 mb-4">
              <h2 className="text-lg font-semibold text-primary">Edit Application</h2>
              <p className="text-sm text-tertiary">
                Update the display name and description. Name (slug) and ownership cannot be changed here.
              </p>
            </div>

            <HookForm form={form} onSubmit={onSubmit} className="space-y-5">
              <FormField name="displayName" control={form.control}>
                {({ field, fieldState }) => (
                  <Input
                    label="Display Name"
                    placeholder="Payment Gateway"
                    hint={fieldState.error?.message ?? "Human-friendly name shown in UI."}
                    isInvalid={!!fieldState.error}
                    isRequired
                    {...field}
                  />
                )}
              </FormField>
              <FormField name="description" control={form.control}>
                {({ field, fieldState }) => (
                  <TextArea
                    label="Description"
                    rows={3}
                    placeholder="What this application does"
                    hint={fieldState.error?.message}
                    isInvalid={!!fieldState.error}
                    isRequired
                    {...field}
                  />
                )}
              </FormField>

              <div className="flex justify-end gap-2 pt-2">
                <Button type="button" color="secondary" size="sm" onClick={() => onOpenChange(false)}>
                  Cancel
                </Button>
                <Button type="submit" color="primary" size="sm" isLoading={mutation.isPending}>
                  Save Changes
                </Button>
              </div>
            </HookForm>
          </div>
        </Dialog>
      </Modal>
    </ModalOverlay>
  );
}

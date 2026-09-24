import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { toast } from "sonner";

import { ModalOverlay, Modal, Dialog } from "@/components/application/modals/modal";
import { HookForm } from "@/components/base/form/hook-form";
import { Button } from "@/components/base/buttons/button";

import { editEnvironmentSchema, type EditEnvironmentForm } from "@/features/catalog/schemas/registerEnvironment";
import { EnvironmentFormFields } from "@/features/catalog/components/EnvironmentFormFields";
import { useEditEnvironment, type EnvironmentDetailResponse } from "@/features/catalog/api/environments";
import { applyProblemDetailsToForm, type ProblemDetails } from "@/shared/forms/problemDetails";
import { zodFieldPaths } from "@/shared/forms/zodFieldPaths";

interface Props {
  environment: EnvironmentDetailResponse;
  open: boolean;
  onOpenChange: (open: boolean) => void;
}

const ENVIRONMENT_NAME_CONFLICT_TAIL = "environment-name-conflict";

/**
 * Edit-Environment modal (A2) — mirrors `RegisterEnvironmentDialog`'s field set exactly
 * (no field is immutable, unlike `EditVmDialog`'s teamId omission) and `EditVmDialog`'s
 * concurrency/error-handling shape.
 *
 * Pre-fills from the supplied `environment` via RHF `values` (re-syncs on every prop
 * change, including the post-412 refetch). On submit, calls `useEditEnvironment` which
 * sends `If-Match: "<version>"`.
 *
 * Server-error UX (mirrors EditVmDialog + RegisterEnvironmentDialog):
 *  - 400 ProblemDetails with `errors` map → fields set via `applyProblemDetailsToForm`.
 *  - 409 EnvironmentNameConflict → mapped onto the `displayName` field (a rename collision
 *    is a display-name problem, not a generic toast) — dialog stays open.
 *  - 412 ConcurrencyConflict → toast + dialog stays open; the hook invalidates the detail
 *    query so the parent page refetches and RHF `values` resyncs to the latest state.
 *  - Anything else → generic toast, dialog stays open for retry.
 */
export function EditEnvironmentDialog({ environment, open, onOpenChange }: Props) {
  const mutation = useEditEnvironment(environment.id);

  const form = useForm<EditEnvironmentForm>({
    resolver: zodResolver(editEnvironmentSchema),
    values: {
      displayName: environment.displayName,
      description: environment.description,
      type: environment.type,
      region: environment.region ?? "",
    },
  });

  const onSubmit = form.handleSubmit(async (values) => {
    try {
      await mutation.mutateAsync({
        values: {
          displayName: values.displayName,
          description: values.description,
          type: values.type,
          region: values.region || null,
          resourceDetails: environment.resourceDetails,
        },
        expectedVersion: environment.version,
      });
      toast.success("Environment updated");
      onOpenChange(false);
    } catch (err) {
      const problem = err as ProblemDetails & { __status?: number };
      const status = problem.__status;

      const handled = applyProblemDetailsToForm(problem, (name, error) =>
        form.setError(name as Parameters<typeof form.setError>[0], error),
        zodFieldPaths(editEnvironmentSchema),
      );
      if (handled) return;

      const tail = typeof problem.type === "string" ? problem.type.split("/").pop() : undefined;
      if (tail === ENVIRONMENT_NAME_CONFLICT_TAIL) {
        form.setError("displayName", {
          type: "server",
          message: problem.detail ?? "An environment with this name already exists.",
        });
        return;
      }

      if (status === 412) {
        toast.error("Someone else edited this. Reload to see the latest values.");
        return;
      }

      const detail = problem.detail ?? problem.title ?? "Could not update environment";
      toast.error(detail);
    }
  });

  return (
    <ModalOverlay isOpen={open} onOpenChange={onOpenChange} isDismissable={!mutation.isPending}>
      <Modal className="max-w-[560px]">
        <Dialog aria-label="Edit Environment" className="bg-primary rounded-xl shadow-xl p-6 outline-none">
          <div className="w-full">
            <div className="space-y-1 mb-4">
              <h2 className="text-lg font-semibold text-primary">Edit Environment</h2>
              <p className="text-sm text-tertiary">Update the environment's details.</p>
            </div>

            <HookForm form={form} onSubmit={onSubmit} className="space-y-5">
              <EnvironmentFormFields control={form.control} idPrefix="edit-environment" disabled={mutation.isPending} />

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

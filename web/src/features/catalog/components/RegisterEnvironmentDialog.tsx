import { useEffect } from "react";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { toast } from "sonner";

import { ModalOverlay, Modal, Dialog } from "@/components/application/modals/modal";
import { HookForm, FormField } from "@/components/base/form/hook-form";
import { Input } from "@/components/base/input/input";
import { TextArea } from "@/components/base/textarea/textarea";
import { Button } from "@/components/base/buttons/button";
import { Avatar } from "@/components/base/avatar/avatar";

import {
  registerEnvironmentSchema,
  environmentTypes,
  type RegisterEnvironmentForm,
} from "@/features/catalog/schemas/registerEnvironment";
import { environmentTypeLabel } from "@/features/catalog/components/EnvironmentTable";
import { useRegisterEnvironment, type RegisterEnvironmentRequest } from "@/features/catalog/api/environments";
import { applyProblemDetailsToForm, type ProblemDetails } from "@/shared/forms/problemDetails";
import { zodFieldPaths } from "@/shared/forms/zodFieldPaths";
import { useCurrentUser } from "@/shared/auth/useCurrentUser";
import { initialsOf } from "@/shared/auth/initials";

interface Props {
  open: boolean;
  onOpenChange: (open: boolean) => void;
}

const DEFAULT_VALUES: RegisterEnvironmentForm = {
  displayName: "",
  description: "",
  type: "development",
  region: "",
};

// RFC 7807 `type` URI slug for Kartova.SharedKernel.AspNetCore.ProblemTypes.EnvironmentNameConflict
// (backend has no generated FE mirror of ProblemTypes — matched by tail segment, same convention
// as toastProblem's byProblemType tail matching).
const ENVIRONMENT_NAME_CONFLICT_TAIL = "environment-name-conflict";

/**
 * Register-Environment modal (E-02.F-05.S-01). Environments have no owning team (unlike
 * VM/Application/Service/API registration) — no team `<select>`, no "Team is required"
 * side-state. Fields: displayName, description, type, region.
 *
 * On submit, `resourceDetails` is always sent as `null` — `RegisterEnvironmentRequest.resourceDetails`
 * is required-but-nullable on the wire (per-type resource attributes) and this A1 form does not
 * collect it (deferred to a later slice).
 *
 * Server-error UX:
 *  - 400 ProblemDetails with `errors` map → fields set via `applyProblemDetailsToForm`.
 *  - 409 EnvironmentNameConflict → mapped onto the `displayName` field (a name collision is a
 *    display-name problem, not a generic toast) — dialog stays open, matching the 400 UX.
 *  - Anything else → generic toast, dialog stays open for retry.
 */
export function RegisterEnvironmentDialog({ open, onOpenChange }: Props) {
  const user = useCurrentUser();
  const mutation = useRegisterEnvironment();

  const form = useForm<RegisterEnvironmentForm>({
    resolver: zodResolver(registerEnvironmentSchema),
    defaultValues: DEFAULT_VALUES,
  });

  useEffect(() => {
    if (!open) {
      form.reset(DEFAULT_VALUES);
    }
  }, [open, form]);

  const onSubmit = form.handleSubmit(async (values) => {
    const payload: RegisterEnvironmentRequest = {
      displayName: values.displayName,
      description: values.description,
      type: values.type,
      region: values.region || null,
      resourceDetails: null,
    };

    try {
      await mutation.mutateAsync(payload);
      toast.success("Environment registered");
      onOpenChange(false);
    } catch (err) {
      const problem = err as ProblemDetails;
      const handled = applyProblemDetailsToForm(
        problem,
        (name, error) => form.setError(name as Parameters<typeof form.setError>[0], error),
        zodFieldPaths(registerEnvironmentSchema),
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

      toast.error(problem.detail ?? problem.title ?? "Failed to register environment");
    }
  });

  const initials = initialsOf(user?.displayName);

  return (
    <ModalOverlay isOpen={open} onOpenChange={onOpenChange} isDismissable={!mutation.isPending}>
      <Modal className="max-w-[560px]">
        <Dialog aria-label="Register Environment" className="bg-primary rounded-xl shadow-xl p-6 outline-none">
          <div className="w-full">
            <div className="space-y-1 mb-4">
              <h2 className="text-lg font-semibold text-primary">Register Environment</h2>
              <p className="text-sm text-tertiary">Add a new environment to your catalog</p>
            </div>

            <HookForm form={form} onSubmit={onSubmit} className="space-y-5">
              <FormField name="displayName" control={form.control}>
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
              <FormField name="description" control={form.control}>
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
                <FormField name="type" control={form.control}>
                  {({ field }) => (
                    <div className="flex flex-col gap-1">
                      <label htmlFor="register-environment-type" className="text-sm font-medium text-secondary">
                        Type
                      </label>
                      <select
                        id="register-environment-type"
                        data-testid="register-environment-type-select"
                        className="rounded-md border border-secondary px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-brand-500 bg-primary text-primary"
                        value={field.value}
                        onChange={field.onChange}
                        disabled={mutation.isPending}
                      >
                        {environmentTypes.map((t) => (
                          <option key={t} value={t}>{environmentTypeLabel(t)}</option>
                        ))}
                      </select>
                    </div>
                  )}
                </FormField>
                <FormField name="region" control={form.control}>
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

              <div>
                <p className="text-xs uppercase tracking-wide text-tertiary">Created by</p>
                <div className="mt-1 inline-flex items-center gap-2 rounded-md border border-secondary bg-secondary/40 px-2 py-1.5">
                  <Avatar size="xs" initials={initials} />
                  <div className="min-w-0">
                    <div className="text-sm font-medium text-primary truncate">{user?.displayName ?? "—"}</div>
                    <div className="text-xs text-tertiary truncate">{user?.email ?? ""}</div>
                  </div>
                </div>
              </div>

              <div className="flex justify-end gap-2 pt-2">
                <Button type="button" color="secondary" size="sm" onClick={() => onOpenChange(false)}>
                  Cancel
                </Button>
                <Button type="submit" color="primary" size="sm" isLoading={mutation.isPending}>
                  Register Environment
                </Button>
              </div>
            </HookForm>
          </div>
        </Dialog>
      </Modal>
    </ModalOverlay>
  );
}

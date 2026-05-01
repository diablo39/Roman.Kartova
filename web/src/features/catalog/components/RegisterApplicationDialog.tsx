import { useEffect } from "react";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { toast } from "sonner";

import { ModalOverlay, Modal, Dialog } from "@/components/application/modals/modal";
import { HookForm, FormField } from "@/components/base/form/hook-form";
import { Input } from "@/components/base/input/input";
import { TextArea } from "@/components/base/textarea/textarea";
import { Button } from "@/components/base/buttons/button";
import { Badge } from "@/components/base/badges/badges";
import { Avatar } from "@/components/base/avatar/avatar";

import {
  registerApplicationSchema,
  type RegisterApplicationInput,
} from "@/features/catalog/schemas/registerApplication";
import { useRegisterApplication } from "@/features/catalog/api/applications";
import {
  applyProblemDetailsToForm,
  type ProblemDetails,
} from "@/shared/forms/problemDetails";
import { useCurrentUser } from "@/shared/auth/useCurrentUser";
import { initialsOf } from "@/shared/auth/initials";

interface Props {
  open: boolean;
  onOpenChange: (open: boolean) => void;
}

export function RegisterApplicationDialog({ open, onOpenChange }: Props) {
  const user = useCurrentUser();
  const mutation = useRegisterApplication();
  const form = useForm<RegisterApplicationInput>({
    resolver: zodResolver(registerApplicationSchema),
    defaultValues: { name: "", displayName: "", description: "" },
  });

  // useForm lives above <ModalOverlay>, so the form state survives the modal
  // unmount and this reset fires reliably on close.
  useEffect(() => {
    if (!open) {
      form.reset({ name: "", displayName: "", description: "" });
    }
  }, [open, form]);

  const onSubmit = form.handleSubmit(async (values) => {
    try {
      await mutation.mutateAsync(values);
      toast.success("Application registered");
      onOpenChange(false);
    } catch (err) {
      const problem = err as ProblemDetails;
      const handled = applyProblemDetailsToForm(problem, (name, error) =>
        form.setError(name as Parameters<typeof form.setError>[0], error)
      );
      if (!handled) {
        const detail =
          problem.detail ?? problem.title ?? "Failed to register application";
        toast.error(detail);
      }
    }
  });

  const initials = initialsOf(user?.displayName);

  return (
    <ModalOverlay isOpen={open} onOpenChange={onOpenChange} isDismissable={!mutation.isPending}>
      <Modal className="max-w-[560px]">
        <Dialog aria-label="Register Application" className="bg-primary rounded-xl shadow-xl p-6 outline-none">

          <div className="w-full">
            <div className="space-y-1 mb-4">
              <h2 className="text-lg font-semibold text-primary">Register Application</h2>
              <p className="text-sm text-tertiary">Add a new application to your catalog</p>
            </div>

            <HookForm form={form} onSubmit={onSubmit} className="space-y-5">
              <FormField name="name" control={form.control}>
                {({ field, fieldState }) => (
                  <Input
                    label="Name"
                    placeholder="payment-gateway"
                    hint={fieldState.error?.message ?? "Lowercase, kebab-case. Used in URLs and CLI."}
                    isInvalid={!!fieldState.error}
                    isRequired
                    {...field}
                  />
                )}
              </FormField>
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
                    placeholder="Short summary..."
                    hint={fieldState.error?.message}
                    isInvalid={!!fieldState.error}
                    isRequired
                    {...field}
                  />
                )}
              </FormField>

              <div className="grid grid-cols-2 gap-4">
                <div>
                  <p className="text-xs uppercase tracking-wide text-tertiary">Owner</p>
                  <div className="mt-1 inline-flex items-center gap-2 rounded-md border border-secondary bg-secondary/40 px-2 py-1.5">
                    <Avatar size="xs" initials={initials} />
                    <div className="min-w-0">
                      <div className="text-sm font-medium text-primary truncate">{user?.displayName ?? "—"}</div>
                      <div className="text-xs text-tertiary truncate">{user?.email ?? ""}</div>
                    </div>
                  </div>
                </div>
                <div>
                  <p className="text-xs uppercase tracking-wide text-tertiary">Lifecycle</p>
                  <Badge color="success" type="pill-color" size="sm" className="mt-1">Active</Badge>
                </div>
              </div>

              <div className="flex justify-end gap-2 pt-2">
                <Button type="button" color="secondary" size="sm" onClick={() => onOpenChange(false)}>
                  Cancel
                </Button>
                <Button type="submit" color="primary" size="sm" isLoading={mutation.isPending}>
                  Register Application
                </Button>
              </div>
            </HookForm>
          </div>
        </Dialog>
      </Modal>
    </ModalOverlay>
  );
}

import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { toast } from "sonner";

import { ModalOverlay, Modal, Dialog } from "@/components/application/modals/modal";
import { HookForm, FormField } from "@/components/base/form/hook-form";
import { Input } from "@/components/base/input/input";
import { TextArea } from "@/components/base/textarea/textarea";
import { Button } from "@/components/base/buttons/button";

import {
  updateTeamSchema,
  type UpdateTeamInput,
} from "@/features/teams/schemas/updateTeam";
import {
  useUpdateTeam,
  type TeamDetailResponse,
  type TeamResponse,
} from "@/features/teams/api/teams";
import {
  applyProblemDetailsToForm,
  type ProblemDetails,
} from "@/shared/forms/problemDetails";

interface Props {
  team: TeamDetailResponse | TeamResponse;
  open: boolean;
  onOpenChange: (open: boolean) => void;
}

/**
 * Rename / edit-team modal — mirrors EditApplicationDialog shape.
 *
 * Pre-fills `displayName` + `description` from the supplied team via RHF
 * `values`. On 400 ProblemDetails: field errors applied; dialog stays open.
 */
export function RenameTeamDialog({ team, open, onOpenChange }: Props) {
  const mutation = useUpdateTeam(team.id);
  const form = useForm<UpdateTeamInput>({
    resolver: zodResolver(updateTeamSchema),
    values: {
      displayName: team.displayName,
      description: team.description ?? "",
    },
  });

  const onSubmit = form.handleSubmit(async (values) => {
    try {
      await mutation.mutateAsync({
        displayName: values.displayName,
        description: values.description ?? "",
      });
      toast.success("Team updated");
      onOpenChange(false);
    } catch (err) {
      const problem = err as ProblemDetails;
      const handled = applyProblemDetailsToForm(problem, (name, error) =>
        form.setError(name as Parameters<typeof form.setError>[0], error),
      );
      if (!handled) {
        const detail = problem.detail ?? problem.title ?? "Could not update team";
        toast.error(detail);
      }
    }
  });

  return (
    <ModalOverlay isOpen={open} onOpenChange={onOpenChange} isDismissable={!mutation.isPending}>
      <Modal className="max-w-[560px]">
        <Dialog aria-label="Edit Team" className="bg-primary rounded-xl shadow-xl p-6 outline-none">
          <div className="w-full">
            <div className="space-y-1 mb-4">
              <h2 className="text-lg font-semibold text-primary">Edit Team</h2>
              <p className="text-sm text-tertiary">Update the display name and description.</p>
            </div>

            <HookForm form={form} onSubmit={onSubmit} className="space-y-5">
              <FormField name="displayName" control={form.control}>
                {({ field, fieldState }) => (
                  <Input
                    label="Display Name"
                    placeholder="Platform Team"
                    hint={fieldState.error?.message ?? "Human-friendly team name shown in UI."}
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
                    placeholder="What this team is responsible for"
                    hint={fieldState.error?.message}
                    isInvalid={!!fieldState.error}
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

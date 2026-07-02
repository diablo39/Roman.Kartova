import { useState } from "react";
import { toast } from "sonner";

import { ModalOverlay, Modal, Dialog } from "@/components/application/modals/modal";
import { Button } from "@/components/base/buttons/button";
import { Checkbox } from "@/components/base/checkbox/checkbox";

import {
  useDecommissionApplication,
  type ApplicationResponse,
} from "@/features/catalog/api/applications";
import { isLifecycle, lifecycleLabel } from "@/features/catalog/lifecycle";
import { usePermissions } from "@/shared/auth/usePermissions";
import { KartovaPermissions } from "@/shared/auth/permissions";
import type { ProblemDetails } from "@/shared/forms/problemDetails";

interface Props {
  application: ApplicationResponse;
  open: boolean;
  onOpenChange: (open: boolean) => void;
}

/**
 * Decommission confirmation modal — terminal-state warning.
 *
 * No form — the user only confirms the destructive action. OrgAdmins holding
 * `catalog.applications.lifecycle.override` may additionally tick "Override
 * sunset date" (ADR-0073) while the app is still before its sunset date; the
 * checkbox disappears once sunset has elapsed (override is then meaningless).
 *
 * Server-error UX:
 *  - 409 with `reason="before-sunset-date"` (server-supplied) and a
 *    `sunsetDate` extension → toast with the date, close.
 *  - 409 generic → toast with the server-reported `currentLifecycle`, close.
 *  - Anything else → fallback toast; dialog stays open for retry.
 */
export function DecommissionConfirmDialog({ application, open, onOpenChange }: Props) {
  const mutation = useDecommissionApplication(application.id);
  const { hasPermission } = usePermissions();
  const [overrideSunset, setOverrideSunset] = useState(false);

  const canOverride =
    hasPermission(KartovaPermissions.CatalogApplicationsLifecycleOverride) &&
    !!application.sunsetDate &&
    new Date(application.sunsetDate) > new Date();

  const onConfirm = async () => {
    try {
      await mutation.mutateAsync(canOverride ? { overrideSunset } : undefined);
      toast.success(`${application.displayName} decommissioned.`);
      onOpenChange(false);
    } catch (err) {
      const problem = err as ProblemDetails & {
        __status?: number;
        reason?: string;
        sunsetDate?: string;
        currentLifecycle?: string;
      };
      const status = problem.__status;

      if (status === 409) {
        if (problem.reason === "before-sunset-date" && problem.sunsetDate) {
          const dateLabel = new Date(problem.sunsetDate).toLocaleDateString();
          toast.error(`Cannot decommission before sunset date ${dateLabel}.`);
          onOpenChange(false);
          return;
        }
        const current = isLifecycle(problem.currentLifecycle)
          ? lifecycleLabel(problem.currentLifecycle)
          : "an unexpected state";
        toast.error(`Cannot decommission — current state is ${current}.`);
        onOpenChange(false);
        return;
      }

      const detail = problem.detail ?? problem.title ?? "Could not decommission application";
      toast.error(detail);
    }
  };

  return (
    <ModalOverlay isOpen={open} onOpenChange={onOpenChange} isDismissable={!mutation.isPending}>
      <Modal className="max-w-[480px]">
        <Dialog aria-label="Decommission Application" className="bg-primary rounded-xl shadow-xl p-6 outline-none">
          <div className="space-y-1 mb-4">
            <h2 className="text-lg font-semibold text-primary">Decommission {application.displayName}?</h2>
            <p className="text-sm text-tertiary">
              This is a terminal state. The application will be hidden from default views and become read-only. This cannot be undone in the current product version.
            </p>
          </div>

          {canOverride && (
            <div className="mb-4">
              <Checkbox
                label="Override sunset date"
                hint="Decommission now even though the sunset date hasn't passed yet. This is audit-logged."
                isSelected={overrideSunset}
                onChange={setOverrideSunset}
              />
            </div>
          )}

          <div className="flex justify-end gap-2 pt-2">
            <Button type="button" color="secondary" size="sm" onClick={() => onOpenChange(false)}>
              Cancel
            </Button>
            <Button
              type="button"
              color="primary-destructive"
              size="sm"
              isLoading={mutation.isPending}
              onClick={onConfirm}
            >
              Decommission
            </Button>
          </div>
        </Dialog>
      </Modal>
    </ModalOverlay>
  );
}

import { useMemo } from "react";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { toast } from "sonner";

import { Button } from "@/components/base/buttons/button";
import { Card, CardContent } from "@/components/base/card/card";
import { HookForm, FormField } from "@/components/base/form/hook-form";
import { Input } from "@/components/base/input/input";
import { TextArea } from "@/components/base/textarea/textarea";

import {
  useOrgProfile,
  useUpdateOrgProfile,
  useLogoUrl,
} from "@/features/organization/api/organization";
import {
  orgProfileSchema,
  type OrgProfileInput,
} from "@/features/organization/schemas/orgProfile";
import { LogoUploader } from "@/features/organization/components/LogoUploader";
import { usePermissions } from "@/shared/auth/usePermissions";
import { KartovaPermissions } from "@/shared/auth/permissions";
import {
  applyProblemDetailsToForm,
  type ProblemDetails,
} from "@/shared/forms/problemDetails";

/**
 * Computes the list of IANA time zones the runtime knows about. Memoized at
 * module load via `useMemo` inside the component (the underlying array is
 * stable per-runtime, so we only need to materialize it once per render). The
 * list is ~420 entries — large but flat; a dedicated picker can follow in a
 * later UX pass.
 */
function useTimeZones(currentValue?: string): string[] {
  return useMemo(() => {
    const supported = Intl.supportedValuesOf("timeZone");
    // The canonical Intl list excludes deprecated aliases ("UTC", "Etc/UTC",
    // "Etc/GMT") — but the C# backend uses TimeZoneInfo and the seeded
    // default is "UTC", so we must let the user re-pick the value the
    // server returned. Prepend known aliases plus the current saved value
    // when it would otherwise be missing.
    const aliases = ["UTC", "Etc/UTC", "Etc/GMT"];
    const seen = new Set(supported);
    const prefix: string[] = [];
    for (const a of aliases) if (!seen.has(a)) prefix.push(a);
    if (currentValue && !seen.has(currentValue) && !prefix.includes(currentValue)) {
      prefix.push(currentValue);
    }
    return [...prefix, ...supported];
  }, [currentValue]);
}

/**
 * `/settings/organization` — top-level page for editing the current
 * organization's profile (display name, description, default time zone)
 * and its logo. Route wiring lands in F7 of slice 9.
 *
 * Permission model: `org.profile.read` is implicit for any signed-in user
 * via `/me`; this page mirrors the team-edit pattern where the inputs stay
 * visible-but-disabled for users who lack `org.profile.edit`, so non-admins
 * can audit what the org currently looks like without being forced to a
 * separate read-only view.
 *
 * Concurrency: F2's `useUpdateOrgProfile` accepts an `ifMatch` token. The
 * backend reserves the header today and will start enforcing it once the
 * EF concurrency token lands; for now the page simply does not pass it,
 * which the hook handles by omitting the header (see hook docstring).
 *
 * Server-error UX (matches `EditApplicationDialog.tsx` precedent):
 *   - 400 → `applyProblemDetailsToForm` puts errors next to fields,
 *           form stays open.
 *   - 412 → toast + form stays open. (Once concurrency is enforced.)
 *   - 409 → toast + form stays open. (Reserved for future lifecycle.)
 *   - default → toast generic + form stays open for retry.
 */
export function OrganizationSettingsPage() {
  const profileQuery = useOrgProfile();
  const updateMutation = useUpdateOrgProfile();
  const logoUrl = useLogoUrl();
  const { hasPermission, isLoading: permissionsLoading } = usePermissions();
  const canEdit =
    !permissionsLoading && hasPermission(KartovaPermissions.OrgProfileEdit);

  const timeZones = useTimeZones(profileQuery.data?.defaultTimeZone);

  const profile = profileQuery.data;

  // RHF `values` re-syncs the form whenever the profile changes (e.g., after
  // a successful save invalidates the query and a fresh `OrgProfileResponse`
  // arrives). Mirrors `EditApplicationDialog.tsx:53`.
  const form = useForm<OrgProfileInput>({
    resolver: zodResolver(orgProfileSchema),
    values: profile
      ? {
          displayName: profile.displayName,
          description: profile.description ?? "",
          defaultTimeZone: profile.defaultTimeZone,
        }
      : undefined,
  });

  const onSubmit = form.handleSubmit(async (values) => {
    // Collapse empty-string description back to null on the wire — the API
    // accepts both, but the canonical "no description" representation in the
    // OrgProfileResponse is null, so a round-trip GET → submit → GET should
    // not flip the persisted shape.
    const description =
      values.description == null || values.description.trim() === ""
        ? null
        : values.description;
    try {
      await updateMutation.mutateAsync({
        displayName: values.displayName,
        description,
        defaultTimeZone: values.defaultTimeZone,
      });
      toast.success("Organization profile saved");
    } catch (err) {
      const problem = err as ProblemDetails & { __status?: number };
      const status = problem.__status;

      const handled = applyProblemDetailsToForm(problem, (name, error) =>
        form.setError(name as Parameters<typeof form.setError>[0], error),
      );
      if (handled) return; // 400 — field errors set, form stays open.

      if (status === 412) {
        toast.error(
          "Someone else edited the organization. Reload to see the latest values.",
        );
        return;
      }
      if (status === 409) {
        toast.error("Organization state conflicts with this change.");
        return;
      }

      const detail = problem.detail ?? problem.title ?? "Could not save organization profile";
      toast.error(detail);
    }
  });

  // ----- LOADING / ERROR --------------------------------------------------
  if (profileQuery.isLoading) {
    return (
      <Card className="mx-auto max-w-md">
        <CardContent className="p-6 text-center text-sm text-tertiary">
          Loading…
        </CardContent>
      </Card>
    );
  }

  if (profileQuery.isError || !profile) {
    return (
      <Card className="mx-auto max-w-md">
        <CardContent className="space-y-3 p-6 text-center">
          <p className="text-base font-medium text-error-primary">
            Failed to load organization profile
          </p>
          <p className="text-sm text-tertiary">
            Reload the page or try again later.
          </p>
        </CardContent>
      </Card>
    );
  }

  // ----- MAIN VIEW --------------------------------------------------------
  const submitting = updateMutation.isPending;
  const inputsDisabled = !canEdit || submitting;

  return (
    <div className="space-y-8">
      <div>
        <h2 className="text-2xl font-semibold text-primary">Organization Settings</h2>
        <p className="mt-1 text-sm text-tertiary">
          Profile details and logo for your organization.
        </p>
      </div>

      <Card>
        <CardContent>
          <div className="mb-4">
            <h3 className="text-lg font-semibold text-primary">Profile</h3>
            <p className="mt-1 text-sm text-tertiary">
              Display name, description, and default time zone.
            </p>
          </div>

          <HookForm form={form} onSubmit={onSubmit} className="space-y-5">
            <FormField name="displayName" control={form.control}>
              {({ field, fieldState }) => (
                <Input
                  label="Display Name"
                  placeholder="Acme Corporation"
                  hint={fieldState.error?.message ?? "Human-friendly name shown across the app."}
                  isInvalid={!!fieldState.error}
                  isRequired
                  isDisabled={inputsDisabled}
                  {...field}
                />
              )}
            </FormField>

            <FormField name="description" control={form.control}>
              {({ field, fieldState }) => (
                <TextArea
                  label="Description"
                  rows={3}
                  placeholder="What your organization does"
                  hint={fieldState.error?.message ?? "Optional — shown on the dashboard and shared links."}
                  isInvalid={!!fieldState.error}
                  isDisabled={inputsDisabled}
                  {...field}
                  // RHF gives us `value: null | string | undefined`; the
                  // native textarea wants a string.
                  value={field.value ?? ""}
                />
              )}
            </FormField>

            <FormField name="defaultTimeZone" control={form.control}>
              {({ field, fieldState }) => (
                <div className="flex flex-col gap-1.5">
                  <label
                    htmlFor="org-timezone"
                    className="text-sm font-medium text-secondary"
                  >
                    Default Time Zone <span className="text-error-primary">*</span>
                  </label>
                  <select
                    id="org-timezone"
                    className="rounded-lg border border-secondary bg-primary px-3 py-2 text-md text-primary shadow-xs ring-1 ring-primary ring-inset focus:outline-none focus:ring-2 focus:ring-brand-500 disabled:cursor-not-allowed disabled:opacity-50"
                    disabled={inputsDisabled}
                    value={field.value ?? ""}
                    onChange={(e) => field.onChange(e.target.value)}
                    onBlur={field.onBlur}
                    ref={field.ref}
                    name={field.name}
                    aria-invalid={!!fieldState.error}
                  >
                    {timeZones.map((tz) => (
                      <option key={tz} value={tz}>
                        {tz}
                      </option>
                    ))}
                  </select>
                  <p
                    className={
                      fieldState.error
                        ? "text-sm text-error-primary"
                        : "text-sm text-tertiary"
                    }
                  >
                    {fieldState.error?.message ??
                      "IANA time-zone name (e.g. Europe/Oslo)."}
                  </p>
                </div>
              )}
            </FormField>

            <div className="flex justify-end gap-2 pt-2">
              <Button
                type="button"
                color="secondary"
                size="sm"
                isDisabled={submitting || !form.formState.isDirty}
                onClick={() => form.reset()}
              >
                Cancel
              </Button>
              <Button
                type="submit"
                color="primary"
                size="sm"
                isDisabled={!canEdit || submitting}
                isLoading={submitting}
              >
                Save Changes
              </Button>
            </div>
          </HookForm>
        </CardContent>
      </Card>

      <Card>
        <CardContent>
          <LogoUploader currentLogoUrl={logoUrl} canEdit={canEdit} />
        </CardContent>
      </Card>
    </div>
  );
}

export default OrganizationSettingsPage;

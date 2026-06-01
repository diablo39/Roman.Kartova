import { useEffect, useState } from "react";
import { useSearchParams } from "react-router-dom";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { useAuth } from "react-oidc-context";

import { Button } from "@/components/base/buttons/button";
import { Card, CardContent } from "@/components/base/card/card";
import { HookForm, FormField } from "@/components/base/form/hook-form";
import { Input } from "@/components/base/input/input";

import {
  getInvitationAcceptContext,
  acceptInvitation,
  type InvitationAcceptContext,
} from "@/features/invitations/api/acceptInvitation";
import {
  acceptInvitationSchema,
  type AcceptInvitationInput,
} from "@/features/invitations/schemas/acceptInvitation";

// ─── view states ────────────────────────────────────────────────────────────

type ViewState =
  | { kind: "loading" }
  | { kind: "invalid" }
  | { kind: "gone" }
  | { kind: "form"; ctx: InvitationAcceptContext };

// ─── small helper cards ──────────────────────────────────────────────────────

function CardShell({ children }: { children: React.ReactNode }) {
  return (
    <div className="flex h-screen items-center justify-center bg-primary">
      <Card className="max-w-md w-full">
        <CardContent className="space-y-6">
          {children}
        </CardContent>
      </Card>
    </div>
  );
}

function InvalidCard() {
  return (
    <CardShell>
      <h1 className="text-2xl font-semibold text-primary">Invalid invitation link</h1>
      <p className="text-base text-tertiary">
        This invitation link is invalid. Ask the person who invited you to send
        a new one.
      </p>
    </CardShell>
  );
}

function GoneCard() {
  return (
    <CardShell>
      <h1 className="text-2xl font-semibold text-primary">This invitation can no longer be used</h1>
      <p className="text-base text-tertiary">
        This invitation link has expired, been revoked, or was already used. Ask the person who invited you to send a new one.
      </p>
    </CardShell>
  );
}

// ─── form view ───────────────────────────────────────────────────────────────

interface FormViewProps {
  token: string;
  ctx: InvitationAcceptContext;
  onGone: () => void;
}

function FormView({ token, ctx, onGone }: FormViewProps) {
  const auth = useAuth();
  const [globalError, setGlobalError] = useState<string | null>(null);

  const form = useForm<AcceptInvitationInput>({
    resolver: zodResolver(acceptInvitationSchema),
    defaultValues: {
      displayName: ctx.defaultDisplayName,
      password: "",
      confirmPassword: "",
    },
  });

  const onSubmit = form.handleSubmit(async (values) => {
    setGlobalError(null);
    try {
      await acceptInvitation({
        token,
        password: values.password,
        displayName: values.displayName,
      });
      await auth.signinRedirect({ login_hint: ctx.email });
    } catch (err) {
      const status = (err as { __status?: number }).__status;
      if (status === 400) {
        form.setError("password", {
          message: "Password does not meet requirements.",
        });
        return;
      }
      if (status === 410) {
        onGone();
        return;
      }
      setGlobalError("Something went wrong. Please try again.");
    }
  });

  return (
    <CardShell>
      <div className="space-y-1">
        <h1 className="text-2xl font-semibold text-primary">
          Join {ctx.orgDisplayName}
        </h1>
        <p className="text-base text-tertiary">
          {ctx.invitedByDisplayName} invited you
        </p>
      </div>

      {/* Read-only email */}
      <div className="space-y-1.5">
        <label className="text-sm font-medium text-secondary">Email</label>
        <input
          type="email"
          value={ctx.email}
          readOnly
          aria-label="Email"
          className="w-full rounded-lg border border-secondary bg-tertiary px-3 py-2 text-md text-primary opacity-75 ring-1 ring-primary ring-inset cursor-default"
        />
      </div>

      {/* Role indicator */}
      <div className="space-y-1.5">
        <label className="text-sm font-medium text-secondary">Role</label>
        <p className="text-md text-primary">{ctx.role}</p>
      </div>

      <HookForm form={form} onSubmit={onSubmit} className="space-y-4">
        <FormField name="displayName" control={form.control}>
          {({ field, fieldState }) => (
            <Input
              label="Display name"
              placeholder="Your name"
              hint={fieldState.error?.message}
              isInvalid={!!fieldState.error}
              isRequired
              {...field}
            />
          )}
        </FormField>

        <FormField name="password" control={form.control}>
          {({ field, fieldState }) => (
            <Input
              label="Password"
              type="password"
              placeholder="At least 12 characters"
              hint={fieldState.error?.message}
              isInvalid={!!fieldState.error}
              isRequired
              {...field}
            />
          )}
        </FormField>

        <FormField name="confirmPassword" control={form.control}>
          {({ field, fieldState }) => (
            <Input
              label="Confirm password"
              type="password"
              placeholder="Repeat your password"
              hint={fieldState.error?.message}
              isInvalid={!!fieldState.error}
              isRequired
              {...field}
            />
          )}
        </FormField>

        {globalError && (
          <p className="text-sm text-error-primary">{globalError}</p>
        )}

        <Button
          type="submit"
          color="primary"
          size="md"
          isLoading={form.formState.isSubmitting}
          className="w-full"
        >
          Join {ctx.orgDisplayName}
        </Button>
      </HookForm>
    </CardShell>
  );
}

// ─── page ────────────────────────────────────────────────────────────────────

/**
 * `/accept-invitation?token=<token>` — anonymous route (slice-9 spec §6).
 *
 * Flow:
 *  1. If `token` query param is absent → render "Invalid invitation link".
 *  2. Fetch `getInvitationAcceptContext(token)`:
 *     - 410 → "This invitation can no longer be used" message (expired/revoked/already-used).
 *     - other error / 404 → "Invalid invitation link".
 *     - success → render the set-password + display-name form.
 *  3. On submit → `acceptInvitation({ token, password, displayName })`:
 *     - success → `auth.signinRedirect({ login_hint: email })`.
 *     - 400 → password field error.
 *     - 410 → switch to "This invitation can no longer be used" view.
 *     - other → generic form-level error.
 */
export function AcceptInvitationPage() {
  const [searchParams] = useSearchParams();
  const token = searchParams.get("token");

  // Derive initial view: if there is no token we already know the state,
  // so skip loading to avoid a synchronous setState-in-effect lint violation.
  const [view, setView] = useState<ViewState>(() =>
    token ? { kind: "loading" } : { kind: "invalid" },
  );

  useEffect(() => {
    // No token → initial state already set to "invalid"; nothing to fetch.
    if (!token) return;

    let cancelled = false;

    getInvitationAcceptContext(token)
      .then((ctx) => {
        if (!cancelled) setView({ kind: "form", ctx });
      })
      .catch((err) => {
        if (cancelled) return;
        const status = (err as { __status?: number }).__status;
        if (status === 410) {
          setView({ kind: "gone" });
        } else {
          setView({ kind: "invalid" });
        }
      });

    return () => {
      cancelled = true;
    };
  }, [token]);

  if (view.kind === "loading") {
    return (
      <div className="flex h-screen items-center justify-center bg-primary">
        <p className="text-base text-tertiary">Loading…</p>
      </div>
    );
  }

  if (view.kind === "invalid") return <InvalidCard />;
  if (view.kind === "gone") return <GoneCard />;

  return (
    <FormView
      token={token!}
      ctx={view.ctx}
      onGone={() => setView({ kind: "gone" })}
    />
  );
}

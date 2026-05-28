import { useEffect, useRef, useState } from "react";
import { Copy01, Check } from "@untitledui/icons";
import { Button } from "@/components/base/buttons/button";

interface Props {
  url: string;
  email: string;
}

/**
 * Renders the success-state panel for `InviteUserDialog`. Single responsibility:
 * give the OrgAdmin a one-click way to copy the invite URL so they can share
 * it manually (slice-9 spec §6.7 — email delivery deferred to E-06a).
 *
 * The "Copied!" affordance flips for 2 seconds after a successful copy and
 * reverts. We track a timeout ref so a rapid re-click resets the countdown
 * rather than collapsing two flickers into one.
 */
export function CopyInviteLinkBox({ url, email }: Props) {
  const [copied, setCopied] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const timeoutRef = useRef<ReturnType<typeof setTimeout> | null>(null);

  useEffect(() => {
    // Clear any in-flight timeout on unmount so we don't setState on an
    // unmounted component (React would log a warning).
    return () => {
      if (timeoutRef.current !== null) clearTimeout(timeoutRef.current);
    };
  }, []);

  const onCopy = async () => {
    setError(null);
    try {
      await navigator.clipboard.writeText(url);
      setCopied(true);
      if (timeoutRef.current !== null) clearTimeout(timeoutRef.current);
      timeoutRef.current = setTimeout(() => setCopied(false), 2000);
    } catch {
      // Clipboard API can reject in non-secure contexts or when the browser
      // denies the permission; surface a static message rather than letting
      // the error swallow the success state silently.
      setError("Could not copy to clipboard. Select the link manually.");
    }
  };

  return (
    <div className="space-y-4">
      <div className="space-y-1">
        <h3 className="text-base font-semibold text-primary">
          Invitation created for {email}.
        </h3>
        <p className="text-sm text-tertiary">
          Share this link with them — it expires in 7 days.
        </p>
      </div>

      <div className="space-y-2">
        <label
          htmlFor="invite-url"
          className="block text-xs font-medium uppercase tracking-wide text-tertiary"
        >
          Invite link
        </label>
        <div className="flex gap-2">
          <input
            id="invite-url"
            type="text"
            readOnly
            value={url}
            onFocus={(e) => e.currentTarget.select()}
            className="flex-1 rounded-lg border border-secondary bg-secondary px-3 py-2 font-mono text-xs text-primary ring-1 ring-primary ring-inset"
          />
          <Button
            type="button"
            color={copied ? "secondary" : "primary"}
            size="sm"
            onClick={onCopy}
            iconLeading={copied ? Check : Copy01}
          >
            {copied ? "Copied!" : "Copy invite link"}
          </Button>
        </div>
        {error && (
          <p className="text-sm text-error-primary" role="alert">
            {error}
          </p>
        )}
      </div>

      <div className="space-y-1 rounded-lg bg-secondary p-3 text-xs text-tertiary">
        <p>
          <strong className="font-medium text-secondary">
            Email delivery is coming soon.
          </strong>{" "}
          For now, share this link with the recipient via your usual channel
          (chat, email, ticket).
        </p>
        <p>
          The link is single-use and expires after 7 days. If it lapses, revoke
          the pending invitation and create a new one.
        </p>
      </div>
    </div>
  );
}

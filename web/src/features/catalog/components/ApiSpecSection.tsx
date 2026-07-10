import { lazy, Suspense, useEffect, useMemo, useRef, useState } from "react";
import type { ReactNode } from "react";
import { Badge } from "@/components/base/badges/badges";
import { Button } from "@/components/base/buttons/button";
import { useApiSpec } from "@/features/catalog/api/apis";
import type { ApiResponse } from "@/features/catalog/api/apis";
import { AttachApiSpecDialog } from "./AttachApiSpecDialog";
import { detectSpecKind } from "./openapi/detectSpecKind";
import { usePermissions } from "@/shared/auth/usePermissions";
import { KartovaPermissions } from "@/shared/auth/permissions";

const OpenApiRender = lazy(() => import("./openapi/OpenApiRender"));

export function ApiSpecSection({ api }: { api: ApiResponse }) {
  const hasSpec = api.hasSpec ?? false;
  const spec = useApiSpec(api.id, hasSpec);
  const [dialogOpen, setDialogOpen] = useState(false);
  const canWrite = usePermissions().hasPermission(KartovaPermissions.CatalogApisRegister);

  const formatLabel = (m: string) => (m.includes("yaml") ? "YAML" : "JSON");

  const content = spec.data?.content;
  const mediaType = spec.data?.mediaType;
  // Memoized: detectSpecKind parses the (potentially large) spec, so recompute
  // only when the spec text changes — not on every unrelated re-render.
  const kind = useMemo(() => (content ? detectSpecKind(content, mediaType) : "other"), [content, mediaType]);

  // Rebuilt only when the spec changes — consistent with `kind`'s memoization.
  const rawView = useMemo(
    () =>
      content !== undefined && mediaType !== undefined ? (
        <>
          <div className="flex items-center gap-2">
            <Badge type="pill-color" color="gray" size="sm">{formatLabel(mediaType)}</Badge>
            <CopyButton text={content} />
          </div>
          <pre className="max-h-[480px] overflow-auto rounded-md border border-secondary bg-secondary/30 p-3 font-mono text-xs text-primary whitespace-pre-wrap break-words">
            {content}
          </pre>
        </>
      ) : null,
    [content, mediaType],
  );

  return (
    <section className="space-y-3">
      <div className="flex items-center justify-between gap-3">
        <h3 className="text-sm font-medium text-tertiary">Spec document</h3>
        {canWrite && (
          <Button type="button" color="secondary" size="sm" onClick={() => setDialogOpen(true)}>
            {hasSpec ? "Replace" : "Attach spec"}
          </Button>
        )}
      </div>

      {hasSpec ? (
        <div className="space-y-2">
          {spec.data &&
            (kind === "openapi" ? (
              <SpecViews content={spec.data.content} mediaType={spec.data.mediaType} rawView={rawView} />
            ) : (
              rawView
            ))}
          {spec.isLoading && <p className="text-sm text-tertiary">Loading spec…</p>}
          {spec.isError && <p className="text-sm text-error-primary">Couldn't load the spec.</p>}
          {!spec.isLoading && !spec.isError && !spec.data && (
            <p className="text-sm text-tertiary italic">Spec unavailable.</p>
          )}
        </div>
      ) : (
        <p className="text-sm text-tertiary italic">No spec attached.</p>
      )}

      <AttachApiSpecDialog apiId={api.id} open={dialogOpen} onOpenChange={setDialogOpen} hasExistingSpec={hasSpec} />
    </section>
  );
}

function SpecViews({ content, mediaType, rawView }: { content: string; mediaType: string; rawView: ReactNode }) {
  const [view, setView] = useState<"rendered" | "raw">("rendered");
  const tab = (id: "rendered" | "raw", label: string) => (
    <button
      type="button"
      aria-pressed={view === id}
      onClick={() => setView(id)}
      className={`rounded-md px-2.5 py-1 text-xs font-medium ${
        view === id ? "bg-secondary text-primary" : "text-tertiary hover:text-primary"
      }`}
    >
      {label}
    </button>
  );
  return (
    <div className="space-y-2">
      <div className="inline-flex gap-1 rounded-lg border border-secondary p-0.5" role="group" aria-label="Spec view">
        {tab("rendered", "Rendered")}
        {tab("raw", "Raw")}
      </div>
      {view === "raw" ? (
        rawView
      ) : (
        <Suspense fallback={<p className="text-sm text-tertiary">Loading rendered spec…</p>}>
          {/* key on content: a replaced/corrected spec gets a fresh boundary, never a stuck fallback. */}
          <OpenApiRender key={content} content={content} mediaType={mediaType} rawFallback={rawView} />
        </Suspense>
      )}
    </div>
  );
}

function CopyButton({ text }: { text: string }) {
  const [copied, setCopied] = useState(false);
  const timerRef = useRef<ReturnType<typeof setTimeout> | undefined>(undefined);

  useEffect(() => {
    return () => clearTimeout(timerRef.current);
  }, []);

  return (
    <Button
      type="button"
      color="tertiary"
      size="sm"
      onClick={() => {
        void navigator.clipboard.writeText(text);
        setCopied(true);
        clearTimeout(timerRef.current);
        timerRef.current = setTimeout(() => setCopied(false), 1500);
      }}
    >
      {copied ? "Copied" : "Copy"}
    </Button>
  );
}

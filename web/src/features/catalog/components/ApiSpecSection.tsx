import { useState } from "react";
import { Badge } from "@/components/base/badges/badges";
import { Button } from "@/components/base/buttons/button";
import { useApiSpec } from "@/features/catalog/api/apis";
import type { ApiResponse } from "@/features/catalog/api/apis";
import { AttachApiSpecDialog } from "./AttachApiSpecDialog";
import { usePermissions } from "@/shared/auth/usePermissions";
import { KartovaPermissions } from "@/shared/auth/permissions";

export function ApiSpecSection({ api }: { api: ApiResponse }) {
  const hasSpec = api.hasSpec ?? false;
  const spec = useApiSpec(api.id, hasSpec);
  const [dialogOpen, setDialogOpen] = useState(false);
  const canWrite = usePermissions().hasPermission(KartovaPermissions.CatalogApisRegister);

  const formatLabel = (m: string) => (m.includes("yaml") ? "YAML" : "JSON");

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
          {spec.data && (
            <>
              <div className="flex items-center gap-2">
                <Badge type="pill-color" color="gray" size="sm">{formatLabel(spec.data.mediaType)}</Badge>
                <CopyButton text={spec.data.content} />
              </div>
              <pre className="max-h-[480px] overflow-auto rounded-md border border-secondary bg-secondary/30 p-3 font-mono text-xs text-primary whitespace-pre-wrap break-all">
                {spec.data.content}
              </pre>
            </>
          )}
          {spec.isLoading && <p className="text-sm text-tertiary">Loading spec…</p>}
        </div>
      ) : (
        <p className="text-sm text-tertiary italic">No spec attached.</p>
      )}

      <AttachApiSpecDialog apiId={api.id} open={dialogOpen} onOpenChange={setDialogOpen} hasExistingSpec={hasSpec} />
    </section>
  );
}

function CopyButton({ text }: { text: string }) {
  const [copied, setCopied] = useState(false);
  return (
    <Button
      type="button"
      color="tertiary"
      size="sm"
      onClick={() => {
        void navigator.clipboard.writeText(text);
        setCopied(true);
        setTimeout(() => setCopied(false), 1500);
      }}
    >
      {copied ? "Copied" : "Copy"}
    </Button>
  );
}

import { useEffect, useState } from "react";
import { toast } from "sonner";
import { ModalOverlay, Modal, Dialog } from "@/components/application/modals/modal";
import { TextArea } from "@/components/base/textarea/textarea";
import { Button } from "@/components/base/buttons/button";
import { useUpsertApiSpec } from "@/features/catalog/api/apis";
import type { ProblemDetails } from "@/shared/forms/problemDetails";

type MediaType = "application/json" | "application/yaml";

export function inferMediaType(fileName: string | undefined, content: string): MediaType {
  const ext = fileName?.toLowerCase().split(".").pop();
  if (ext === "yaml" || ext === "yml") return "application/yaml";
  if (ext === "json") return "application/json";
  return content.trimStart().startsWith("{") ? "application/json" : "application/yaml";
}

interface Props {
  apiId: string;
  open: boolean;
  onOpenChange: (open: boolean) => void;
  hasExistingSpec: boolean;
}

export function AttachApiSpecDialog({ apiId, open, onOpenChange, hasExistingSpec }: Props) {
  const mutation = useUpsertApiSpec(apiId);
  const [content, setContent] = useState("");
  const [fileName, setFileName] = useState<string | undefined>(undefined);
  const [mediaType, setMediaType] = useState<MediaType>("application/json");
  const [error, setError] = useState<string>("");

  useEffect(() => {
    if (!open) {
      // eslint-disable-next-line react-hooks/set-state-in-effect
      setContent("");
      setFileName(undefined);
      setMediaType("application/json");
      setError("");
    }
  }, [open]);

  const onFile = async (file: File | undefined) => {
    if (!file) return;
    const text = await file.text();
    setFileName(file.name);
    setContent(text);
    setMediaType(inferMediaType(file.name, text));
  };

  const onSubmit = async () => {
    if (content.trim().length === 0) {
      setError("Spec content must not be empty");
      return;
    }
    setError("");
    try {
      await mutation.mutateAsync({ content, mediaType });
      toast.success(hasExistingSpec ? "Spec replaced" : "Spec attached");
      onOpenChange(false);
    } catch (err) {
      const problem = err as ProblemDetails & { message?: string };
      setError(problem.detail ?? problem.message ?? "Failed to save spec.");
    }
  };

  const title = hasExistingSpec ? "Replace spec" : "Attach spec";

  return (
    <ModalOverlay isOpen={open} onOpenChange={onOpenChange} isDismissable={!mutation.isPending}>
      <Modal className="max-w-[640px]">
        <Dialog aria-label={title} className="bg-primary rounded-xl shadow-xl p-6 outline-none">
          <div className="w-full space-y-5">
            <div className="space-y-1">
              <h2 className="text-lg font-semibold text-primary">{title}</h2>
              <p className="text-sm text-tertiary">Upload a file or paste an OpenAPI / AsyncAPI document (JSON or YAML).</p>
            </div>

            <div className="flex flex-col gap-1">
              <label htmlFor="spec-file" className="text-sm font-medium text-secondary">File</label>
              <input id="spec-file" type="file" accept=".json,.yaml,.yml" data-testid="spec-file-input"
                onChange={(e) => void onFile(e.target.files?.[0])} disabled={mutation.isPending}
                className="text-sm text-secondary" />
            </div>

            <TextArea label="…or paste spec content" rows={10} value={content}
              onChange={(v) => { setContent(v); setMediaType(inferMediaType(fileName, v)); }}
              isDisabled={mutation.isPending} />

            <div className="flex flex-col gap-1">
              <label htmlFor="spec-media-type" className="text-sm font-medium text-secondary">Format</label>
              <select id="spec-media-type" data-testid="spec-media-type-select"
                className="rounded-md border border-secondary px-3 py-2 text-sm bg-primary text-primary"
                value={mediaType} onChange={(e) => setMediaType(e.target.value as MediaType)} disabled={mutation.isPending}>
                <option value="application/json">JSON</option>
                <option value="application/yaml">YAML</option>
              </select>
            </div>

            {error && <p className="text-xs text-error-primary" role="alert">{error}</p>}

            <div className="flex justify-end gap-2 pt-2">
              <Button type="button" color="secondary" size="sm" onClick={() => onOpenChange(false)}>Cancel</Button>
              <Button type="button" color="primary" size="sm" isLoading={mutation.isPending} onClick={() => void onSubmit()}>
                {title}
              </Button>
            </div>
          </div>
        </Dialog>
      </Modal>
    </ModalOverlay>
  );
}

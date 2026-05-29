import { useEffect, useMemo, useRef, useState } from "react";
import { toast } from "sonner";

import { Button } from "@/components/base/buttons/button";
import {
  useUploadOrgLogo,
  useDeleteOrgLogo,
} from "@/features/organization/api/organization";
import { toastProblem } from "@/shared/forms/toastProblem";

/**
 * Allowed MIME types for the organization logo — must match the server-side
 * `IsAcceptedMime` list (see slice-9 spec §4 and the OrganizationsController).
 * The 256 KB ceiling is the spec-mandated client guard; the server will also
 * reject larger uploads with 413, but checking first avoids a network round
 * trip for the common "user picked a 5 MB photo" mistake.
 */
const ALLOWED_MIME = ["image/png", "image/jpeg", "image/svg+xml"] as const;
const MAX_BYTES = 256 * 1024;

type AllowedMime = (typeof ALLOWED_MIME)[number];

function isAllowedMime(value: string): value is AllowedMime {
  return (ALLOWED_MIME as readonly string[]).includes(value);
}

interface Props {
  /**
   * Current logo URL composed by `useLogoUrl()` — null when no logo has been
   * uploaded for this organization yet.
   */
  currentLogoUrl: string | null;
  /**
   * Whether the calling user holds `org.profile.edit`. When false, the
   * component renders only the existing logo (or a "No logo" placeholder)
   * and never exposes upload/delete affordances.
   */
  canEdit: boolean;
}

/**
 * Logo upload + delete panel for `OrganizationSettingsPage`.
 *
 * UX:
 *   - Drag-and-drop OR native file picker (the same `<input type="file">`
 *     drives both, so accept= and disabled= behave consistently).
 *   - Client-side validation BEFORE any network call: 256 KB ceiling and
 *     PNG/JPEG/SVG MIME allowlist.
 *   - Selected file shows a 64×64 thumbnail and a 200×200 preview, both
 *     `object-contain` so the aspect ratio is preserved (Stitch mock uses
 *     a circular crop at 64 and a rounded square at 200).
 *   - `URL.createObjectURL` is paired with a cleanup effect so blob URLs
 *     are revoked when the user picks a new file or unmounts; otherwise
 *     each preview leaks a few KB.
 *   - On success the F2 `useUploadOrgLogo` / `useDeleteOrgLogo` hooks
 *     invalidate `orgKeys.profile()`, so the parent page re-renders with
 *     the new etag automatically; no local cache management here.
 *   - 404 from delete is treated as success — the end state (no logo) is
 *     the same and surfacing a "Not found" toast confuses users who
 *     clicked "Remove logo" with that exact intent.
 */
export function LogoUploader({ currentLogoUrl, canEdit }: Props) {
  const uploadMutation = useUploadOrgLogo();
  const deleteMutation = useDeleteOrgLogo();

  const [stagedFile, setStagedFile] = useState<File | null>(null);
  const [isDragging, setIsDragging] = useState(false);
  const fileInputRef = useRef<HTMLInputElement | null>(null);

  // Derive the preview URL from the staged file directly — `useMemo` paired
  // with a `useEffect` cleanup is the recommended React pattern for a value
  // that requires teardown (here `URL.revokeObjectURL`). We can't call
  // `setState` inside the effect (lint enforces it), and we don't want the
  // preview to lag the staged-file state by one render either.
  const previewUrl = useMemo(
    () => (stagedFile ? URL.createObjectURL(stagedFile) : null),
    [stagedFile],
  );
  useEffect(() => {
    // Revoke whenever previewUrl changes (i.e. file swapped) or on unmount.
    return () => {
      if (previewUrl) URL.revokeObjectURL(previewUrl);
    };
  }, [previewUrl]);

  function validateAndStage(file: File): boolean {
    if (!isAllowedMime(file.type)) {
      toast.error("Logo must be PNG, JPEG, or SVG.");
      return false;
    }
    if (file.size > MAX_BYTES) {
      toast.error("Logo must be 256 KB or smaller.");
      return false;
    }
    setStagedFile(file);
    return true;
  }

  function onFilePicked(files: FileList | null) {
    if (!files || files.length === 0) return;
    const file = files[0];
    if (file) validateAndStage(file);
  }

  function onDrop(e: React.DragEvent<HTMLDivElement>) {
    e.preventDefault();
    setIsDragging(false);
    if (!canEdit) return;
    const file = e.dataTransfer.files?.[0];
    if (file) validateAndStage(file);
  }

  async function onUpload() {
    if (!stagedFile) return;
    try {
      await uploadMutation.mutateAsync({
        bytes: stagedFile,
        mimeType: stagedFile.type,
      });
      toast.success("Logo uploaded");
      setStagedFile(null);
      if (fileInputRef.current) fileInputRef.current.value = "";
    } catch (err) {
      // The upload hook constructs a synthetic error with only `__status` and
      // `message` (no `detail` / `title`), so byStatus is the right dispatch:
      // there is no server-supplied detail to prefer at any of these codes.
      toastProblem(err, {
        byStatus: {
          413: "Logo is too large for the server.",
          415: "Server rejected the logo MIME type.",
          422: "Logo rejected by server.",
        },
        fallback: "Could not upload logo.",
      });
    }
  }

  async function onRemove() {
    try {
      await deleteMutation.mutateAsync();
      toast.success("Logo removed");
    } catch (err) {
      const status = (err as { __status?: number }).__status;
      // 404 = "no logo to remove" — the user's intent ("end state: no logo")
      // is already satisfied, so we surface success instead of an error toast.
      if (status === 404) {
        toast.success("Logo removed");
        return;
      }
      toastProblem(err, { fallback: "Could not remove logo." });
    }
  }

  // ----- READ-ONLY VIEW ----------------------------------------------------
  if (!canEdit) {
    return (
      <div className="space-y-3">
        <h3 className="text-lg font-semibold text-primary">Logo</h3>
        {currentLogoUrl ? (
          <img
            src={currentLogoUrl}
            alt="Organization logo"
            className="h-16 w-16 rounded-md object-contain ring-1 ring-secondary"
          />
        ) : (
          <p className="text-sm text-tertiary italic">No logo uploaded.</p>
        )}
      </div>
    );
  }

  // ----- EDITABLE VIEW -----------------------------------------------------
  const showPreview = stagedFile != null && previewUrl != null;
  const uploading = uploadMutation.isPending;
  const deleting = deleteMutation.isPending;

  return (
    <div className="space-y-4">
      <div className="flex items-start justify-between">
        <div>
          <h3 className="text-lg font-semibold text-primary">Logo</h3>
          <p className="mt-1 text-sm text-tertiary">
            PNG, JPEG, or SVG. Maximum 256 KB.
          </p>
        </div>
      </div>

      <div className="grid grid-cols-1 gap-6 md:grid-cols-[1fr_220px]">
        {/* Drop zone + native picker */}
        <div
          data-testid="logo-dropzone"
          onDragOver={(e) => {
            e.preventDefault();
            setIsDragging(true);
          }}
          onDragLeave={() => setIsDragging(false)}
          onDrop={onDrop}
          className={`flex min-h-[160px] cursor-pointer flex-col items-center justify-center rounded-xl border-2 border-dashed p-6 text-center transition ${
            isDragging
              ? "border-brand-500 bg-brand-50"
              : "border-secondary bg-primary"
          }`}
          onClick={() => fileInputRef.current?.click()}
        >
          <p className="text-sm font-medium text-primary">
            {stagedFile ? stagedFile.name : "Drag a logo here, or click to browse"}
          </p>
          <p className="mt-1 text-xs text-tertiary">
            PNG, JPEG, or SVG &mdash; up to 256 KB
          </p>
          <input
            ref={fileInputRef}
            type="file"
            accept={ALLOWED_MIME.join(",")}
            className="hidden"
            aria-label="Choose logo file"
            onChange={(e) => onFilePicked(e.target.files)}
          />
        </div>

        {/* Preview pane */}
        <div className="space-y-3">
          <div className="text-xs uppercase tracking-wide text-tertiary">Preview</div>
          <div className="flex items-end gap-4">
            <div className="flex flex-col items-center gap-1">
              {showPreview ? (
                <img
                  src={previewUrl}
                  alt="Preview 64x64"
                  className="h-16 w-16 rounded-md object-contain ring-1 ring-secondary"
                />
              ) : currentLogoUrl ? (
                <img
                  src={currentLogoUrl}
                  alt="Current logo 64x64"
                  className="h-16 w-16 rounded-md object-contain ring-1 ring-secondary"
                />
              ) : (
                <div className="flex h-16 w-16 items-center justify-center rounded-md bg-secondary text-xs text-tertiary ring-1 ring-secondary">
                  64
                </div>
              )}
              <span className="text-xs text-tertiary">64&times;64</span>
            </div>
            <div className="flex flex-col items-center gap-1">
              {showPreview ? (
                <img
                  src={previewUrl}
                  alt="Preview 200x200"
                  className="h-[200px] w-[200px] rounded-md object-contain ring-1 ring-secondary"
                />
              ) : currentLogoUrl ? (
                <img
                  src={currentLogoUrl}
                  alt="Current logo 200x200"
                  className="h-[200px] w-[200px] rounded-md object-contain ring-1 ring-secondary"
                />
              ) : (
                <div className="flex h-[200px] w-[200px] items-center justify-center rounded-md bg-secondary text-xs text-tertiary ring-1 ring-secondary">
                  200&times;200
                </div>
              )}
              <span className="text-xs text-tertiary">200&times;200</span>
            </div>
          </div>
        </div>
      </div>

      <div className="flex flex-wrap gap-2">
        <Button
          type="button"
          size="sm"
          color="primary"
          isDisabled={!stagedFile || uploading || deleting}
          isLoading={uploading}
          onClick={onUpload}
        >
          Upload Logo
        </Button>
        {stagedFile && (
          <Button
            type="button"
            size="sm"
            color="secondary"
            isDisabled={uploading || deleting}
            onClick={() => {
              setStagedFile(null);
              if (fileInputRef.current) fileInputRef.current.value = "";
            }}
          >
            Cancel
          </Button>
        )}
        {currentLogoUrl && !stagedFile && (
          <Button
            type="button"
            size="sm"
            color="secondary-destructive"
            isDisabled={uploading || deleting}
            isLoading={deleting}
            onClick={onRemove}
          >
            Remove Logo
          </Button>
        )}
      </div>
    </div>
  );
}

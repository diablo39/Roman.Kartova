import { Plus } from "@untitledui/icons";
import { Button } from "@/components/base/buttons/button";
import { Input } from "@/components/base/input/input";
import { PROTOCOLS, PROTOCOL_LABEL, MAX_ENDPOINTS, type EndpointInput } from "@/features/catalog/schemas/registerService";

interface Props {
  value: EndpointInput[];
  onChange: (next: EndpointInput[]) => void;
  disabled?: boolean;
  errors?: (string | undefined)[];
}

export function EndpointsEditor({ value, onChange, disabled = false, errors = [] }: Props) {
  const atMax = value.length >= MAX_ENDPOINTS;

  const updateRow = (index: number, patch: Partial<EndpointInput>) =>
    onChange(value.map((row, i) => (i === index ? { ...row, ...patch } : row)));
  const addRow = () => {
    if (!atMax) onChange([...value, { url: "", protocol: "rest" }]);
  };
  const removeRow = (index: number) => onChange(value.filter((_, i) => i !== index));

  return (
    <div className="flex flex-col gap-3" data-testid="endpoints-editor">
      <div className="flex items-center justify-between">
        <span className="text-sm font-medium text-secondary">Endpoints</span>
        <span className="text-xs text-tertiary">{value.length}/{MAX_ENDPOINTS}</span>
      </div>

      {value.length === 0 && (
        <p className="text-xs text-tertiary">No endpoints yet — a service can be registered without any.</p>
      )}

      {value.map((row, index) => (
        <div key={index} className="flex items-start gap-2">
          <div className="flex-1">
            <Input
              aria-label={`Endpoint ${index + 1} URL`}
              placeholder="https://api.example.com/v1"
              value={row.url}
              onChange={(v: string) => updateRow(index, { url: v })}
              isInvalid={!!errors[index]}
              hint={errors[index]}
              isDisabled={disabled}
            />
          </div>
          <select
            aria-label={`Endpoint ${index + 1} protocol`}
            className="rounded-md border border-secondary px-3 py-2 text-sm bg-primary text-primary disabled:opacity-60"
            value={row.protocol}
            onChange={(e) => updateRow(index, { protocol: e.target.value as EndpointInput["protocol"] })}
            disabled={disabled}
          >
            {PROTOCOLS.map((p) => (
              <option key={p} value={p}>{PROTOCOL_LABEL[p]}</option>
            ))}
          </select>
          <Button
            type="button"
            color="tertiary"
            size="sm"
            aria-label={`Remove endpoint ${index + 1}`}
            onClick={() => removeRow(index)}
            isDisabled={disabled}
          >
            Remove
          </Button>
        </div>
      ))}

      <div>
        <Button type="button" color="secondary" size="sm" iconLeading={Plus} onClick={addRow} isDisabled={disabled || atMax}>
          Add endpoint
        </Button>
        {atMax && <p className="mt-1 text-xs text-tertiary">Maximum of {MAX_ENDPOINTS} endpoints reached.</p>}
      </div>
    </div>
  );
}

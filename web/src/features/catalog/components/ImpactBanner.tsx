export function ImpactBanner(props: {
  total: number;
  tiers: { tier: number; count: number }[];
  truncated: boolean;
  nodeCap: number;
  onClose: () => void;
}) {
  const { total, tiers, truncated, nodeCap, onClose } = props;
  const summary = tiers.map((t) => `${t.count}× tier-${t.tier}`).join(", ");
  return (
    <div className="flex items-center gap-3 rounded-md bg-primary/90 px-3 py-2 text-sm ring-1 ring-secondary">
      <span className="font-medium text-primary">
        {total} downstream{summary ? ` (${summary})` : ""}
      </span>
      {truncated && <span className="text-xs text-warning-primary">showing first {nodeCap}</span>}
      <button
        type="button"
        onClick={onClose}
        className="ml-2 rounded-md border border-secondary px-2 py-1 text-xs text-primary"
      >
        Close Analysis
      </button>
    </div>
  );
}

import { Card } from "@/components/ui/card";

export function CatalogPlaceholder() {
  return (
    <Card className="flex h-full items-center justify-center p-8">
      <div className="text-center">
        <h1 className="text-2xl font-semibold">Catalog</h1>
        <p className="mt-2 text-muted">Coming in Slice 3 — entity registration &amp; browsing.</p>
      </div>
    </Card>
  );
}

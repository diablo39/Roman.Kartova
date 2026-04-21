import { Search } from "lucide-react";

export function TopBar() {
  return (
    <header className="flex h-14 items-center border-b border-border bg-card px-6">
      <div className="flex flex-1 items-center gap-4">
        <div className="relative w-full max-w-xl">
          <Search className="absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-muted" />
          <input
            type="text"
            placeholder="Search entities..."
            disabled
            className="w-full rounded-md border border-border bg-background py-2 pl-9 pr-3 text-sm text-muted placeholder:text-muted disabled:cursor-not-allowed"
          />
        </div>
      </div>
    </header>
  );
}

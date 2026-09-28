import { Outlet } from "react-router-dom";

/** Master-shell page frame: sidebar left, top bar + routed content right. Hook-free (shared by both apps). */
export function ShellLayout({ sidebar, topBar }: { sidebar: React.ReactNode; topBar: React.ReactNode }) {
  return (
    <div className="flex h-full">
      {sidebar}
      <div className="flex flex-1 flex-col overflow-hidden">
        {topBar}
        <main className="flex-1 overflow-auto p-6">
          <Outlet />
        </main>
      </div>
    </div>
  );
}

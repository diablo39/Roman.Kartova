/**
 * Master-shell sidebar chrome (docs/ui-screens/master_shell_expanded): fixed-width aside, "Kartova" logo
 * header, scrollable nav. Nav content is supplied by the app — tenant `Sidebar` or admin `AdminSidebarNav`.
 */
export function SidebarFrame({ children }: { children: React.ReactNode }) {
  return (
    <aside className="flex h-full w-[260px] flex-col border-r border-secondary bg-secondary">
      <div className="flex h-14 items-center border-b border-secondary px-4">
        <span className="text-lg font-semibold text-primary">Kartova</span>
      </div>
      <nav className="flex-1 overflow-y-auto p-3">{children}</nav>
    </aside>
  );
}

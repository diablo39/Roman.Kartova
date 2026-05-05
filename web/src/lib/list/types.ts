export type SortDirection = "asc" | "desc";

export interface CursorListResult<TItem> {
  items: TItem[];
  isLoading: boolean;
  isFetching: boolean;
  isError: boolean;
  hasNext: boolean;
  hasPrev: boolean;
  goNext: () => void;
  goPrev: () => void;
  reset: () => void;
}

export interface CursorPageEnvelope<TItem> {
  items: TItem[];
  nextCursor: string | null;
  prevCursor: string | null;
}

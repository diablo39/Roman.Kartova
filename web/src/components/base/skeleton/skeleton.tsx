import type { HTMLAttributes } from "react";
import { cx } from "@/lib/utils/cx";

export function Skeleton({ className, ...props }: HTMLAttributes<HTMLDivElement>) {
  return <div className={cx("animate-pulse rounded-md bg-secondary", className)} {...props} />;
}

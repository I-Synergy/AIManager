/** Shapes the web layer needs that the shared contract does not express. */
import type { CatalogItem } from "../AiWorkspaceManager.VSCode.Common/types";

export type GroupKey = "attention" | "pending" | "locked" | "synced";

export interface InstalledGroup {
  key: GroupKey;
  label: string;
  items: CatalogItem[];
}

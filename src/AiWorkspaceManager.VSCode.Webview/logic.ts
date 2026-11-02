/**
 * Pure view logic: no Lit, no DOM, no RPC. Everything here is unit-tested with
 * `node --test` against a plain tsc build, so the fiddly parts (filtering,
 * grouping, preset deltas, status wording) are verified without an editor.
 */

import type { CatalogItem, ItemKind, PresetCard, ReleaseRow, UpdateRow } from "../AiWorkspaceManager.VSCode.Common/types";
import type { InstalledGroup } from "./types-web";

export type FilterState = {
  query: string;
  kind: ItemKind | "all";
  state: "all" | "installed" | "available" | "needs-attention";
  source: string;
};

const ATTENTION = new Set([
  "missing",
  "local-modified",
  "upstream-changed",
  "both",
  "removed-upstream",
]);

export function isAttention(status: string): boolean {
  return ATTENTION.has(status);
}

/** The free-text half of every filter: id, kind, path and description. */
function matchesQuery(item: CatalogItem, needle: string): boolean {
  if (!needle) {
    return true;
  }
  return `${item.id} ${item.kind} ${item.path} ${item.description}`
    .toLowerCase().includes(needle);
}

export function filterItems(items: CatalogItem[], filters: FilterState): CatalogItem[] {
  const needle = filters.query.trim().toLowerCase();
  return items.filter((item) => {
    if (filters.kind !== "all" && item.kind !== filters.kind) {
      return false;
    }
    if (filters.source !== "all" && item.source !== filters.source) {
      return false;
    }
    if (filters.state === "installed" && !item.selected) {
      return false;
    }
    if (filters.state === "available" && item.selected) {
      return false;
    }
    if (filters.state === "needs-attention" && !isAttention(item.status)) {
      return false;
    }
    return matchesQuery(item, needle);
  });
}

/* ── installed filters ───────────────────────────────────────────────────── */

export type InstalledState = "all" | "in-sync" | "needs-attention" | "locked" | "pending";

export interface InstalledFilters {
  query: string;
  kind: ItemKind | "all";
  state: InstalledState;
  source: string;
}

/**
 * The Installed view is the selected items, so its state filter is about their
 * health rather than about being selected at all.
 */
export function filterInstalled(
  items: CatalogItem[],
  filters: InstalledFilters,
): CatalogItem[] {
  const needle = filters.query.trim().toLowerCase();
  return items.filter((item) => {
    if (!item.selected) {
      return false;
    }
    if (filters.kind !== "all" && item.kind !== filters.kind) {
      return false;
    }
    if (filters.source !== "all" && item.source !== filters.source) {
      return false;
    }
    if (filters.state === "needs-attention" && !isAttention(item.status)) {
      return false;
    }
    if (filters.state === "in-sync" && item.status !== "ok") {
      return false;
    }
    if (filters.state === "locked" && item.status !== "locked") {
      return false;
    }
    if (filters.state === "pending" && item.status !== "not-installed") {
      return false;
    }
    return matchesQuery(item, needle);
  });
}

/** New-repo helper: everything matching the current filter, installed at once. */
export function idsToInstall(items: CatalogItem[]): string[] {
  return items.filter((item) => !item.selected).map((item) => item.id);
}

/** Human label for a source id; the template bucket reads as its publisher. */
export function sourceLabel(id: string): string {
  return id === "template" ? "I-Synergy" : id;
}

/** One section of a list, grouped by the catalog source an item came from. */
export interface SourceGroup<T> {
  id: string;
  label: string;
  items: T[];
}

/**
 * Group by source for the views that answer "where did this come from?".
 * Declared sources keep the manifest's order; any source the payload does not
 * name is appended alphabetically rather than hidden. With no declared order,
 * groups appear in the order their first item does — callers that sort by
 * severity first keep that ranking in the headings.
 */
export function groupBySource<T extends { source: string }>(
  items: T[],
  order: string[] = [],
): SourceGroup<T>[] {
  const buckets = new Map<string, T[]>();
  for (const item of items) {
    const bucket = buckets.get(item.source) ?? [];
    bucket.push(item);
    buckets.set(item.source, bucket);
  }

  const groups: SourceGroup<T>[] = [];
  const emit = (id: string): void => {
    const bucket = buckets.get(id);
    if (!bucket || bucket.length === 0) {
      return;
    }
    groups.push({ id, label: sourceLabel(id), items: bucket });
    buckets.delete(id);
  };

  if (order.length === 0) {
    for (const id of buckets.keys()) {
      emit(id);
    }
    return groups;
  }
  for (const id of order) {
    emit(id);
  }
  for (const id of [...buckets.keys()].sort((a, b) => sourceLabel(a).localeCompare(sourceLabel(b)))) {
    emit(id);
  }
  return groups;
}

export function groupInstalled(items: CatalogItem[]): InstalledGroup[] {
  const selected = items.filter((item) => item.selected);
  const groups: InstalledGroup[] = [
    { key: "attention", label: "Needs attention", items: selected.filter((item) => isAttention(item.status)) },
    { key: "pending", label: "Selected, not materialised", items: selected.filter((item) => item.status === "not-installed") },
    { key: "locked", label: "Locked", items: selected.filter((item) => item.status === "locked") },
    { key: "synced", label: "In sync", items: selected.filter((item) => item.status === "ok") },
  ];
  return groups.filter((group) => group.items.length > 0);
}

export function statusLabel(status: string, selected = true): string {
  switch (status) {
    case "ok":
      return "in sync";
    case "available":
      return "available";
    case "not-installed":
      return "not installed";
    case "missing":
      return "missing";
    case "local-modified":
      return "modified here";
    case "locked":
      return "locked";
    case "both":
      return "conflict";
    case "upstream-changed":
      return "update";
    case "removed-upstream":
      return selected ? "gone upstream" : "deselected";
    default:
      return status;
  }
}

export function statusClass(status: string): string {
  switch (status) {
    case "ok":
      return "ok";
    case "upstream-changed":
      return "update";
    case "local-modified":
    case "missing":
      return "warn";
    case "locked":
      return "available";
    case "both":
      return "danger";
    default:
      return "available";
  }
}

export interface PresetDelta {
  add: number;
  label: string;
}

/**
 * What adding this preset would change. Presets compose, so there is nothing
 * to remove: items already selected stay selected. With ids from the resolver
 * the count is exact; an older resolver only sent counts, so fall back to a
 * per-kind upper bound rather than claiming nothing new.
 */
export function presetDelta(
  card: PresetCard,
  selectedIds: ReadonlySet<string>,
  installed?: { skills: number; agents: number },
): PresetDelta {
  let add = 0;
  if (card.ids) {
    add = card.ids.reduce((count, id) => count + (selectedIds.has(id) ? 0 : 1), 0);
  } else if (installed) {
    add = Math.max(0, card.skills - installed.skills)
      + Math.max(0, card.agents - installed.agents);
  }
  return { add, label: add ? `${add} to add` : "already selected" };
}

export function pluralise(count: number, singular: string, plural = `${singular}s`): string {
  return `${count} ${count === 1 ? singular : plural}`;
}

/* ── releases ─────────────────────────────────────────────────────────────── */

/**
 * The source whose release state Updates should report: a source with a newer
 * release wins, then any source that publishes releases at all — including one
 * that is unpinned, because "what upstream publishes" is still worth saying.
 */
export function releaseSource(rows: ReleaseRow[]): ReleaseRow | undefined {
  return rows.find((row) => row.checked && row.newer && Boolean(row.latest))
    ?? rows.find((row) => row.checked && Boolean(row.latest));
}

/**
 * One line for the Settings sources table. `—` when nothing is known yet;
 * the reason when the remote could not be read, because "unknown" that looks
 * like "up to date" is exactly the lie this feature exists to avoid.
 */
export function releaseSummary(row: ReleaseRow | undefined): string {
  if (!row) {
    return "—";
  }
  if (!row.checked) {
    return row.error === "offline" ? "offline" : "unknown";
  }
  if (!row.latest) {
    return "no tags";
  }
  if (!row.pinnedTag) {
    // Nothing is pinned, so this is what upstream publishes — not the revision
    // this workspace reads. Saying "latest" here read as "you are on it".
    return `upstream ${row.latest}`;
  }
  if (row.newer) {
    return `${row.current} → ${row.latest}`;
  }
  return `${row.latest} (current)`;
}

/**
 * How the Sources table shows a ref. `local` is the resolver's sentinel for
 * "no pinned revision — read this repository's own tree", not a branch name.
 */
export function sourceRefLabel(ref: string): string {
  return ref === "local" ? "working tree" : ref;
}

/** Banner wording for Updates; null when there is nothing worth saying. */
export type ReleaseNoticeKind = "newer" | "unpinned";

export interface ReleaseNotice {
  kind: ReleaseNoticeKind;
  label: string;
  detail: string;
}

export function releaseNotice(row: ReleaseRow | undefined): ReleaseNotice | null {
  if (!row || !row.checked || !row.latest) {
    return null;
  }
  const candidate = row.latestIsPrerelease ? " (prerelease)" : "";
  if (row.newer) {
    return {
      kind: "newer",
      label: `A newer ${row.id} release exists: ${row.latest}${candidate}`,
      detail: `This project pins ${row.current}. Set "ref" for source "${row.id}" `
        + `in .ai/ai.json to ${row.latest}, then sync.`,
    };
  }
  if (!row.pinnedTag) {
    // We know what upstream publishes, but not which release (if any) this
    // tree corresponds to. Saying so beats "nothing to compare".
    const source = sourceLabel(row.id);
    return {
      kind: "unpinned",
      label: `${source} release ${row.latest}${candidate} is published`,
      detail: row.ref === "local"
        ? `This workspace reads its own tree, so it is not pinned to a release. `
          + `Set "ref" for source "${row.id}" in .ai/ai.json to ${row.latest} to track it.`
        : `This source tracks ${row.ref}, so it is not compared against releases. `
          + `Pin "ref" for source "${row.id}" to ${row.latest} to track it.`,
    };
  }
  return null;
}

/**
 * The Updates empty state. "Everything is up to date" is only honest when the
 * selected items can actually be compared — a local selection has no upstream,
 * and a remote *unselected* source (Copilot) must not lend it one.
 */
export function emptyUpdateNotice(
  remoteSelected: boolean,
): { heading: string; hint: string } {
  if (!remoteSelected) {
    return {
      heading: "Nothing to compare",
      hint: "The selected items come from this repository's own tree (source == target), "
        + "so there is no upstream to diff. Pin a remote ref in .ai/ai.json to track "
        + "template releases, or select items from a third-party source.",
    };
  }
  return {
    heading: "Everything is up to date",
    hint: "No upstream changes and no local modifications were found against the locked revision.",
  };
}

/** One collapsible section of the catalog, in vocabulary order. */
export interface CategoryGroup {
  id: string;
  label: string;
  items: CatalogItem[];
}

/**
 * Group filtered items by category, following the vocabulary's declared order so
 * the list does not reshuffle as counts change. Empty categories are dropped —
 * a filter should not leave a trail of headers — and any category the vocabulary
 * does not know about is appended rather than hidden.
 */
export function groupByCategory(
  items: CatalogItem[],
  categories: { id: string; label: string }[],
): CategoryGroup[] {
  const byCategory = new Map<string, CatalogItem[]>();
  for (const item of items) {
    const bucket = byCategory.get(item.category) ?? [];
    bucket.push(item);
    byCategory.set(item.category, bucket);
  }

  const groups: CategoryGroup[] = [];
  for (const category of categories) {
    const bucket = byCategory.get(category.id);
    if (bucket && bucket.length > 0) {
      groups.push({ id: category.id, label: category.label, items: bucket });
      byCategory.delete(category.id);
    }
  }
  for (const [id, bucket] of [...byCategory.entries()].sort(([a], [b]) => a.localeCompare(b))) {
    if (bucket.length > 0) {
      groups.push({ id, label: id, items: bucket });
    }
  }
  return groups;
}

/** "template:council" → "council"; other sources keep their prefix. */
export function displayId(id: string): string {
  const colon = id.indexOf(":");
  if (colon === -1) {
    return id;
  }
  const source = id.slice(0, colon);
  return source === "template" ? id.slice(colon + 1) : id;
}

/**
 * Identity of a catalog row. An agent and a skill may share a name (`architect`
 * exists as both), so row identity must include the kind — keying on the id
 * alone made clicking the skill select the agent.
 */
export function itemKey(item: { kind: string; id: string }): string {
  return `${item.kind}:${item.id}`;
}

/* ── multi-select ─────────────────────────────────────────────────────────── */

export type PickState = "none" | "some" | "all";

/** What the header checkbox should show for the currently visible rows. */
export function pickState(visibleIds: string[], picked: string[]): PickState {
  if (visibleIds.length === 0) {
    return "none";
  }
  const set = new Set(picked);
  const hits = visibleIds.reduce((count, id) => count + (set.has(id) ? 1 : 0), 0);
  if (hits === 0) {
    return "none";
  }
  return hits === visibleIds.length ? "all" : "some";
}

/**
 * The header checkbox: select or clear every visible id, leaving picks that are
 * outside the current filter alone — filtering must not silently drop a choice.
 */
export function toggleAll(visibleIds: string[], picked: string[], select: boolean): string[] {
  const set = new Set(picked);
  for (const id of visibleIds) {
    if (select) {
      set.add(id);
    } else {
      set.delete(id);
    }
  }
  return [...set];
}

/**
 * Picked ids that a given action can actually act on: install only touches
 * unselected items, remove only touches selected ones.
 */
export function pickedIds(items: CatalogItem[], picked: string[], selected: boolean): string[] {
  const set = new Set(picked);
  return items
    .filter((item) => set.has(item.id) && item.selected === selected)
    .map((item) => item.id);
}

/* ── update filters ──────────────────────────────────────────────────────── */

export type UpdateState = "all" | "ready" | "conflict";

export interface UpdateFilters {
  query: string;
  kind: ItemKind | "all";
  state: UpdateState;
  source: string;
}

/**
 * The Updates payload is two lists (ready / conflicts); filtering keeps that
 * shape so the view can keep rendering both groups. The state filter selects a
 * group rather than a status.
 */
export function filterUpdates(
  ready: UpdateRow[],
  blocked: UpdateRow[],
  filters: UpdateFilters,
): { ready: UpdateRow[]; blocked: UpdateRow[] } {
  const needle = filters.query.trim().toLowerCase();
  const keep = (row: UpdateRow): boolean => {
    if (filters.kind !== "all" && row.kind !== filters.kind) {
      return false;
    }
    if (filters.source !== "all" && row.source !== filters.source) {
      return false;
    }
    if (!needle) {
      return true;
    }
    return `${row.id} ${row.displayId} ${row.path} ${row.status} ${row.source}`
      .toLowerCase().includes(needle);
  };
  return {
    ready: filters.state === "conflict" ? [] : ready.filter(keep),
    blocked: filters.state === "ready" ? [] : blocked.filter(keep),
  };
}

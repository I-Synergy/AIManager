export type Route = "CATALOG" | "SETTINGS";

export type Tab = "browse" | "installed" | "updates" | "presets";

export default class Router extends EventTarget {
  private route: Route = "CATALOG";
  private tab: Tab = "browse";

  get CurrentRoute(): Route {
    return this.route;
  }

  get CurrentTab(): Tab {
    return this.tab;
  }

  Navigate(route: Route): void {
    if (this.route === route) {
      return;
    }
    this.route = route;
    this.dispatchEvent(new Event("route-changed"));
  }

  SelectTab(tab: Tab): void {
    const changed = this.tab !== tab || this.route !== "CATALOG";
    this.tab = tab;
    this.route = "CATALOG";
    if (changed) {
      this.dispatchEvent(new Event("route-changed"));
    }
  }
}

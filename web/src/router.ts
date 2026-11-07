import { useEffect, useState } from "react";

/** The pages of the app. Ids and filters are in the address, so every page can be bookmarked and shared. */
export type Route =
  | { page: "signin"; next?: string }
  | { page: "register"; next?: string }
  | { page: "search"; params: URLSearchParams }
  | { page: "listing"; listingId: string; params: URLSearchParams }
  | { page: "rentals"; as: "renter" | "owner" }
  | { page: "rental"; bookingId: string }
  | { page: "my-listings" }
  | { page: "listing-new" }
  | { page: "listing-edit"; listingId: string }
  | { page: "earnings" }
  | { page: "admin" }
  | { page: "admin-rental"; bookingId: string }
  | { page: "not-found" };

const guid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
const isId = (value: string | undefined): value is string => !!value && guid.test(value);

/** Only addresses within the app are followed after signing in. */
export function safeNext(value: string | null): string | undefined {
  return value && value.startsWith("/") && !value.startsWith("//") ? value : undefined;
}

export function parseRoute(pathname: string, search = ""): Route {
  const params = new URLSearchParams(search);
  const parts = pathname.split("/").filter(Boolean).map(decodeURIComponent);
  const [a, b, c] = parts;
  const n = parts.length;

  if (n === 0) return { page: "search", params };
  if (n === 1 && a === "signin") return { page: "signin", next: safeNext(params.get("next")) };
  if (n === 1 && a === "register") return { page: "register", next: safeNext(params.get("next")) };
  if (n === 2 && a === "l" && isId(b)) return { page: "listing", listingId: b, params };
  if (n === 1 && a === "rentals") return { page: "rentals", as: params.get("as") === "owner" ? "owner" : "renter" };
  if (n === 2 && a === "rentals" && isId(b)) return { page: "rental", bookingId: b };
  if (n === 2 && a === "my" && b === "listings") return { page: "my-listings" };
  if (n === 3 && a === "my" && b === "listings" && c === "new") return { page: "listing-new" };
  if (n === 3 && a === "my" && b === "listings" && isId(c)) return { page: "listing-edit", listingId: c };
  if (n === 1 && a === "earnings") return { page: "earnings" };
  if (n === 1 && a === "admin") return { page: "admin" };
  if (n === 2 && a === "admin" && isId(b)) return { page: "admin-rental", bookingId: b };
  return { page: "not-found" };
}

/** Pages anyone can open, signed in or not. */
export const isPublic = (route: Route) => route.page === "search" || route.page === "listing" || route.page === "not-found";

export function navigate(path: string, replace = false) {
  if (replace) history.replaceState(null, "", path);
  else history.pushState(null, "", path);
  window.dispatchEvent(new PopStateEvent("popstate"));
  if (!replace) window.scrollTo({ top: 0 });
}

export function useRoute(): Route {
  const read = () => parseRoute(location.pathname, location.search);
  const [route, setRoute] = useState<Route>(read);
  useEffect(() => {
    const update = () => setRoute(read());
    window.addEventListener("popstate", update);
    return () => window.removeEventListener("popstate", update);
  }, []);
  return route;
}

/** Props for an anchor that moves within the app without reloading the page. */
export function linkProps(href: string) {
  return {
    href,
    onClick: (e: React.MouseEvent) => {
      if (e.metaKey || e.ctrlKey || e.shiftKey || e.button !== 0) return;
      e.preventDefault();
      navigate(href);
    },
  };
}

/** The sign-in page, coming back here afterwards. */
export const signInHere = () => `/signin?next=${encodeURIComponent(location.pathname + location.search)}`;

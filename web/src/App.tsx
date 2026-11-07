import { KeyRound } from "lucide-react";
import { useCallback, useEffect, useMemo, useState } from "react";
import { api, setToken } from "./api";
import { Shell } from "./components/Shell";
import { Empty, Failed, Loading } from "./components/States";
import { AccountCtx, type Account } from "./context";
import { Admin, AdminRental } from "./pages/Admin";
import { Register, SignIn } from "./pages/Auth";
import { Earnings } from "./pages/Earnings";
import { ListingEdit } from "./pages/ListingEdit";
import { ListingPage } from "./pages/ListingPage";
import { MyListings } from "./pages/MyListings";
import { RentalPage } from "./pages/RentalPage";
import { Rentals } from "./pages/Rentals";
import { SearchPage } from "./pages/SearchPage";
import { isPublic, linkProps, navigate, signInHere, useRoute, type Route } from "./router";
import { loadSession, saveSession } from "./session";
import type { Me, Session } from "./types";

function NotFound() {
  return (
    <Empty title="This page does not exist" icon={<KeyRound size={26} />}>
      <span className="muted">The address may be mistyped, or the listing was taken down.</span>
      <a className="button" {...linkProps("/")}>Find something to rent</a>
    </Empty>
  );
}

function Page({ route, account }: { route: Route; account: Account | null }) {
  switch (route.page) {
    case "search": return <SearchPage params={route.params} />;
    case "listing": return <ListingPage key={route.listingId} listingId={route.listingId} params={route.params} />;
    case "not-found": return <NotFound />;
    default: break;
  }
  if (!account) return null;
  switch (route.page) {
    case "rentals": return <Rentals as={route.as} />;
    case "rental": return <RentalPage key={route.bookingId} bookingId={route.bookingId} />;
    case "my-listings": return <MyListings />;
    case "listing-new": return <ListingEdit />;
    case "listing-edit": return <ListingEdit key={route.listingId} listingId={route.listingId} />;
    case "earnings": return <Earnings />;
    case "admin": return account.me.isAdmin ? <Admin /> : <NotFound />;
    case "admin-rental": return account.me.isAdmin ? <AdminRental key={route.bookingId} bookingId={route.bookingId} /> : <NotFound />;
    default: return <NotFound />;
  }
}

export function App() {
  const route = useRoute();
  const [session, setSession] = useState<Session | null>(() => loadSession());
  const [me, setMe] = useState<Me | null>(null);
  const [error, setError] = useState<string | null>(null);

  const signOut = useCallback(() => {
    saveSession(null);
    setToken(null, () => {});
    setSession(null);
    setMe(null);
    navigate("/", true);
  }, []);

  if (session) setToken(session.accessToken, signOut);

  const refresh = useCallback(async () => {
    const value = await api.me();
    setMe(value);
    return value;
  }, []);

  useEffect(() => {
    if (!session) return;
    setError(null);
    refresh().catch((e: Error) => setError(e.message));
  }, [session, refresh]);

  const open = isPublic(route);
  useEffect(() => {
    const auth = route.page === "signin" || route.page === "register";
    if (!session && !auth && !open) navigate(signInHere(), true);
    if (session && auth) navigate(route.next ?? "/", true);
  }, [session, route, open]);

  const signedIn = useCallback((value: Session) => {
    saveSession(value);
    setToken(value.accessToken, signOut);
    setSession(value);
  }, [signOut]);

  const account = useMemo<Account | null>(() => (session && me ? { session, me, refresh, signOut } : null), [session, me, refresh, signOut]);
  const context = useMemo(() => ({ account, signedIn }), [account, signedIn]);

  if (!session && (route.page === "signin" || route.page === "register")) {
    return <AccountCtx.Provider value={context}>{route.page === "register" ? <Register next={route.next} /> : <SignIn next={route.next} />}</AccountCtx.Provider>;
  }

  if (session && !me) {
    return <div className="center">{error ? <Failed message={error} onRetry={() => refresh().catch((e: Error) => setError(e.message))} /> : <Loading />}</div>;
  }

  return (
    <AccountCtx.Provider value={context}>
      <Shell route={route} wide={route.page === "search"}><Page route={route} account={account} /></Shell>
    </AccountCtx.Provider>
  );
}

import { createContext, useContext } from "react";
import type { Me, Session } from "./types";

export interface Account {
  session: Session;
  me: Me;
  refresh: () => Promise<Me>;
  signOut: () => void;
}

export const AccountCtx = createContext<{ account: Account | null; signedIn: (session: Session) => void } | null>(null);

export function useApp() {
  const value = useContext(AccountCtx);
  if (!value) throw new Error("useApp outside of the app");
  return value;
}

/** The signed-in account, or null on public pages for visitors. */
export const useMaybeAccount = () => useApp().account;

export function useAccount(): Account {
  const { account } = useApp();
  if (!account) throw new Error("useAccount before signing in");
  return account;
}

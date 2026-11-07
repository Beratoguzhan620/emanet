import type { BookingStatus, Category, EventKind } from "./types";

/** Rentals run on Turkish days; screens show Istanbul's calendar wherever the browser is. */
export const ZONE = "Europe/Istanbul";

const money = new Intl.NumberFormat("tr-TR", { style: "currency", currency: "TRY", minimumFractionDigits: 0, maximumFractionDigits: 2 });
const dayTime = new Intl.DateTimeFormat("en-GB", { day: "numeric", month: "short", hour: "2-digit", minute: "2-digit", timeZone: ZONE });
const day = new Intl.DateTimeFormat("en-GB", { weekday: "short", day: "numeric", month: "short", timeZone: "UTC" });
const shortDay = new Intl.DateTimeFormat("en-GB", { day: "numeric", month: "short", timeZone: "UTC" });
const isoParts = new Intl.DateTimeFormat("en-CA", { year: "numeric", month: "2-digit", day: "2-digit", timeZone: ZONE });

export const formatMoney = (value: number) => money.format(value);
export const formatDayTime = (iso: string) => dayTime.format(new Date(iso));
/** A calendar date such as "2026-11-07", which has no time zone of its own. */
export const formatDay = (date: string) => day.format(new Date(`${date}T00:00:00Z`));
export const formatShortDay = (date: string) => shortDay.format(new Date(`${date}T00:00:00Z`));
export const km = (value: number) => (value < 1 ? `${Math.round(value * 1000)} m` : `${value.toFixed(1)} km`);

/** Today in Turkey as yyyy-mm-dd, plus some days. */
export function today(plusDays = 0, now = Date.now()): string {
  return addDays(isoParts.format(new Date(now)), plusDays);
}

export function addDays(date: string, days: number): string {
  const d = new Date(`${date}T00:00:00Z`);
  d.setUTCDate(d.getUTCDate() + days);
  return d.toISOString().slice(0, 10);
}

export const daysBetween = (start: string, end: string) => Math.round((Date.parse(`${end}T00:00:00Z`) - Date.parse(`${start}T00:00:00Z`)) / 86_400_000);

/** "11–14 Nov", or "28 Nov – 2 Dec" across months; the end is the return day. */
export function formatRange(start: string, end: string): string {
  const a = new Date(`${start}T00:00:00Z`);
  const b = new Date(`${end}T00:00:00Z`);
  if (a.getUTCMonth() === b.getUTCMonth()) return `${a.getUTCDate()}–${shortDay.format(b)}`;
  return `${shortDay.format(a)} – ${shortDay.format(b)}`;
}

export function initials(name: string): string {
  const words = name.trim().split(/\s+/).filter(Boolean);
  return ((words[0]?.[0] ?? "") + (words.length > 1 ? words[words.length - 1]![0] : "")).toLocaleUpperCase("tr-TR") || "?";
}

/** "in 12 min", "3 h ago": how far a moment is from now, roughly. */
export function relative(iso: string, now = Date.now()): string {
  const diff = new Date(iso).getTime() - now;
  const abs = Math.abs(diff);
  if (abs < 45_000) return diff >= 0 ? "now" : "just now";
  const unit = abs < 3_600_000 ? [Math.round(abs / 60_000), "min"] as const : abs < 172_800_000 ? [Math.round(abs / 3_600_000), "h"] as const : [Math.round(abs / 86_400_000), "days"] as const;
  const text = `${Math.max(1, unit[0])} ${unit[1]}`;
  return diff >= 0 ? `in ${text}` : `${text} ago`;
}

export const plural = (n: number, word: string, many = `${word}s`) => `${n} ${n === 1 ? word : many}`;

export const categoryLabel: Record<Category, string> = {
  Tools: "Tools", Garden: "Garden", Camping: "Camping", Sports: "Sports", Photo: "Photo and video", Electronics: "Electronics",
  Music: "Music", Party: "Party and events", Kids: "Kids", Travel: "Travel", Other: "Other",
};

export const categories = Object.keys(categoryLabel) as Category[];

export const statusLabel: Record<BookingStatus, string> = {
  Requested: "Waiting for the owner", Confirmed: "Confirmed", Declined: "Declined", Expired: "Not answered", Cancelled: "Cancelled",
  Active: "With the renter", Returned: "Returned", Disputed: "Damage claim", Completed: "Completed",
};

export const statusTone: Record<BookingStatus, "plain" | "info" | "ok" | "danger" | "accent"> = {
  Requested: "info", Confirmed: "accent", Declined: "plain", Expired: "plain", Cancelled: "plain", Active: "accent", Returned: "accent",
  Disputed: "danger", Completed: "ok",
};

export const eventLabel: Record<EventKind, string> = {
  Requested: "Requested", Confirmed: "Confirmed", Declined: "Declined", Expired: "Ran out", Cancelled: "Cancelled", Refunded: "Refunded",
  RentCharged: "Rent charged", HoldsReleased: "Holds let go", PickedUp: "Picked up", Returned: "Given back", DamageReported: "Damage reported",
  ClaimAccepted: "Claim accepted", ClaimRejected: "Claim rejected", ClaimSettled: "Claim decided", DepositCharged: "Deposit charged",
  DepositReleased: "Deposit let go", Completed: "Completed", Reviewed: "Reviewed",
};

export const isOpen = (status: BookingStatus) => ["Requested", "Confirmed", "Active", "Returned", "Disputed"].includes(status);

/** Rent for some days with the weekly discount, as the server works it out (rounded to the kuruş). */
export function rentFor(pricePerDay: number, weeklyPercentOff: number, days: number): number {
  const full = Math.round(pricePerDay * 100) * days;
  const discounted = days >= 7 && weeklyPercentOff > 0 ? Math.round((full * (100 - weeklyPercentOff)) / 100) : full;
  return discounted / 100;
}

/** The test cards of the stand-in payment provider. No card number is ever typed or sent. */
export const paymentMethods = [
  { token: "pm_card_ok", label: "Test card that pays" },
  { token: "pm_card_declined", label: "Test card that is refused" },
  { token: "pm_card_no_funds", label: "Test card with too little money" },
] as const;

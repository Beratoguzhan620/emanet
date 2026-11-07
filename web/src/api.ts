import type {
  Booking, BookingStatus, BookingSummary, Books, CalendarRange, Category, CategoryCount, Dispute, Earnings, HandoverPhoto, Listing, ListingInput,
  ListingSummary, Me, Page, Photo, Quote, Review, Session, Sort, Stage,
} from "./types";

/** An API error with the stable code from the problem response, such as "unavailable". */
export class ApiError extends Error {
  constructor(
    message: string,
    public readonly status: number,
    public readonly code: string | null,
    public readonly fields: Record<string, string[]> = {},
  ) {
    super(message);
  }

  /** The request never reached the server: the device is offline or the server is down. */
  get offline() {
    return this.status === 0;
  }
}

let token: string | null = null;
let onUnauthorized: () => void = () => {};

export function setToken(value: string | null, unauthorized: () => void) {
  token = value;
  onUnauthorized = unauthorized;
}

async function send(path: string, init: RequestInit = {}): Promise<Response> {
  const headers = new Headers(init.headers);
  if (typeof init.body === "string") headers.set("Content-Type", "application/json");
  if (token && !headers.has("Authorization")) headers.set("Authorization", `Bearer ${token}`);

  let response: Response;
  try {
    response = await fetch(path, { ...init, headers });
  } catch (e) {
    if ((e as Error).name === "AbortError") throw e;
    throw new ApiError("The server cannot be reached. Check your connection.", 0, null);
  }

  if (response.status === 401 && token) onUnauthorized();
  if (!response.ok) {
    const problem = await response.json().catch(() => null);
    const fields = (problem?.errors ?? {}) as Record<string, string[]>;
    const fallback = response.status === 429 ? "Too many requests. Wait a minute and try again." : "Something went wrong. Try again.";
    const detail = problem?.detail ?? Object.values(fields).flat()[0] ?? fallback;
    throw new ApiError(detail, response.status, problem?.code ?? null, fields);
  }
  return response;
}

async function request<T>(path: string, init: RequestInit = {}): Promise<T> {
  const response = await send(path, init);
  return response.status === 204 ? (undefined as T) : ((await response.json()) as T);
}

const json = (body: unknown) => JSON.stringify(body);
const post = (body: unknown = {}): RequestInit => ({ method: "POST", body: json(body) });
const put = (body: unknown): RequestInit => ({ method: "PUT", body: json(body) });
const image = (file: Blob): RequestInit => ({ method: "POST", body: file, headers: { "Content-Type": file.type || "application/octet-stream" } });
const bk = (id: string) => `/api/bookings/${id}`;

export function query(params: Record<string, string | number | boolean | undefined | null>): string {
  const search = new URLSearchParams();
  for (const [key, value] of Object.entries(params)) {
    if (value !== undefined && value !== null && String(value).trim() !== "") search.set(key, String(value).trim());
  }
  const text = search.toString();
  return text ? `?${text}` : "";
}

export interface SearchFilters {
  q?: string; category?: Category; lat?: number; lng?: number; radiusKm?: number; minPrice?: number; maxPrice?: number;
  from?: string; to?: string; sort?: Sort; page?: number; pageSize?: number;
}

export const api = {
  register: (body: { email: string; displayName: string; password: string }) => request<Session>("/api/auth/register", post(body)),
  login: (email: string, password: string) => request<Session>("/api/auth/login", post({ email, password })),
  me: () => request<Me>("/api/me"),

  search: (f: SearchFilters) => request<Page<ListingSummary>>(`/api/listings${query({ ...f })}`),
  categories: () => request<CategoryCount[]>("/api/listings/categories"),
  listing: (id: string) => request<Listing>(`/api/listings/${id}`),
  myListings: () => request<ListingSummary[]>("/api/me/listings"),
  createListing: (body: ListingInput) => request<Listing>("/api/listings", post(body)),
  updateListing: (id: string, body: ListingInput) => request<Listing>(`/api/listings/${id}`, put(body)),
  pause: (id: string) => request<Listing>(`/api/listings/${id}/pause`, post()),
  resume: (id: string) => request<Listing>(`/api/listings/${id}/resume`, post()),
  removeListing: (id: string) => request<void>(`/api/listings/${id}`, { method: "DELETE" }),
  addPhoto: (id: string, file: Blob) => request<Photo>(`/api/listings/${id}/photos`, image(file)),
  orderPhotos: (id: string, photoIds: string[]) => request<void>(`/api/listings/${id}/photos/order`, put({ photoIds })),
  removePhoto: (id: string, photoId: string) => request<void>(`/api/listings/${id}/photos/${photoId}`, { method: "DELETE" }),
  reviews: (id: string) => request<Page<Review>>(`/api/listings/${id}/reviews?pageSize=20`),

  quote: (id: string, start: string, end: string) => request<Quote>(`/api/listings/${id}/quote${query({ start, end })}`),
  calendar: (id: string, from?: string) => request<CalendarRange[]>(`/api/listings/${id}/calendar${query({ from })}`),
  block: (id: string, start: string, end: string, note: string) => request<CalendarRange>(`/api/listings/${id}/blocks`, post({ start, end, note })),
  unblock: (id: string, entryId: string) => request<void>(`/api/listings/${id}/blocks/${entryId}`, { method: "DELETE" }),

  book: (body: { listingId: string; start: string; end: string; message: string; paymentMethod: string }) => request<Booking>("/api/bookings", post(body)),
  booking: (id: string) => request<Booking>(bk(id)),
  myBookings: (role: "renter" | "owner", status?: BookingStatus[]) =>
    request<Page<BookingSummary>>(`/api/me/bookings${query({ role, status: status?.join(","), pageSize: 100 })}`),
  accept: (id: string) => request<Booking>(`${bk(id)}/accept`, post()),
  decline: (id: string, reason: string) => request<Booking>(`${bk(id)}/decline`, post({ reason })),
  cancel: (id: string, reason: string) => request<Booking>(`${bk(id)}/cancel`, post({ reason })),
  pickUp: (id: string, code: string) => request<Booking>(`${bk(id)}/pickup`, post({ code })),
  giveBack: (id: string, code: string) => request<Booking>(`${bk(id)}/return`, post({ code })),
  allGood: (id: string) => request<Booking>(`${bk(id)}/all-good`, post()),
  claim: (id: string, amount: number, note: string) => request<Booking>(`${bk(id)}/claim`, post({ amount, note })),
  acceptClaim: (id: string) => request<Booking>(`${bk(id)}/claim/accept`, post()),
  rejectClaim: (id: string, note: string) => request<Booking>(`${bk(id)}/claim/reject`, post({ note })),
  review: (id: string, stars: number, comment: string) => request<Review>(`${bk(id)}/review`, post({ stars, comment })),
  addConditionPhoto: (id: string, stage: Stage, file: Blob) => request<HandoverPhoto>(`${bk(id)}/photos${query({ stage })}`, image(file)),
  /** Condition photos are private, so they are fetched with the token and shown from memory. */
  photoBlob: async (url: string) => URL.createObjectURL(await (await send(url)).blob()),

  earnings: () => request<Earnings>("/api/me/earnings"),
  books: () => request<Books>("/api/admin/books"),
  disputes: () => request<Dispute[]>("/api/admin/disputes"),
  adminBooking: (id: string) => request<Booking>(`/api/admin/bookings/${id}`),
  resolve: (id: string, amount: number, note: string) => request<Booking>(`/api/admin/bookings/${id}/resolve`, post({ amount, note })),
};

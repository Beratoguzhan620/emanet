export interface User { id: string; email: string; displayName: string }
export interface Session { accessToken: string; expiresAt: string; user: User }
export interface Me { user: User; memberSince: string; activeListings: number; isAdmin: boolean }
export interface Page<T> { items: T[]; total: number; pageNumber: number; pageSize: number }

export type Category = "Tools" | "Garden" | "Camping" | "Sports" | "Photo" | "Electronics" | "Music" | "Party" | "Kids" | "Travel" | "Other";
export type ListingStatus = "Active" | "Paused" | "Removed";
export type Sort = "Relevance" | "Distance" | "PriceLow" | "PriceHigh" | "Newest";

export interface Place { latitude: number; longitude: number; area: string; exact: boolean }
export interface Photo { id: string; url: string; thumbUrl: string; width: number; height: number }
export interface Owner { id: string; name: string; memberSince: string; activeListings: number }

export interface ListingSummary {
  id: string; title: string; category: Category; pricePerDay: number; weeklyPercentOff: number; deposit: number; area: string;
  latitude: number; longitude: number; distanceKm: number | null; thumbUrl: string | null; photos: number; rating: number | null;
  ratingCount: number; ownerName: string; status: ListingStatus;
}

export interface Listing {
  id: string; title: string; description: string; category: Category; pricePerDay: number; weeklyPercentOff: number; deposit: number;
  minDays: number; maxDays: number; instantBook: boolean; place: Place; photos: Photo[]; owner: Owner; rating: number | null; ratingCount: number;
  status: ListingStatus; isMine: boolean; createdAt: string; updatedAt: string;
}

export interface ListingInput {
  title: string; description: string; category: Category; pricePerDay: number; weeklyPercentOff: number; deposit: number;
  latitude: number; longitude: number; area: string; minDays: number; maxDays: number; instantBook: boolean;
}

export interface CategoryCount { key: Category; activeListings: number }

export interface Quote {
  start: string; end: string; days: number; pricePerDay: number; fullPrice: number; discount: number; rent: number; deposit: number;
  available: boolean; reason: string | null; instantBook: boolean;
}

export interface CalendarRange { id: string | null; start: string; end: string; kind: "Taken" | "Booked" | "Closed"; note: string | null }

export type BookingStatus = "Requested" | "Confirmed" | "Declined" | "Expired" | "Cancelled" | "Active" | "Returned" | "Disputed" | "Completed";
export type EventKind =
  | "Requested" | "Confirmed" | "Declined" | "Expired" | "Cancelled" | "Refunded" | "RentCharged" | "HoldsReleased" | "PickedUp" | "Returned"
  | "DamageReported" | "ClaimAccepted" | "ClaimRejected" | "ClaimSettled" | "DepositCharged" | "DepositReleased" | "Completed" | "Reviewed";
export type Stage = "Pickup" | "Return" | "Damage";

export interface Party { id: string; name: string; rating: number | null; ratingCount: number }
export interface BookingListing { id: string; title: string; thumbUrl: string | null; area: string }
export interface Payment { rentCharged: boolean; refund: number; refunded: boolean; holdsReleased: boolean; depositCharged: number; depositSettled: boolean }
export interface BookingEvent { id: number; kind: EventKind; actorName: string | null; at: string; detail: string }
export interface Handover {
  pickupCode: string | null; returnCode: string | null; pickedUpAt: string | null; returnedAt: string | null; claimBy: string | null;
  canPickUp: boolean; canGiveBack: boolean; canReportDamage: boolean; canSignOff: boolean;
}
export interface Claim {
  amount: number; note: string; claimedAt: string; answerBy: string; answer: "Accepted" | "Rejected" | null; answerNote: string; taken: number | null; canAnswer: boolean;
}
export interface HandoverPhoto { id: string; stage: Stage; by: string; url: string; thumbUrl: string; width: number; height: number; at: string }
export interface Review { id: string; authorName: string; stars: number; comment: string; at: string }

export interface Booking {
  id: string; listing: BookingListing; owner: Party; renter: Party; start: string; end: string; days: number; rent: number; deposit: number;
  fee: number | null; ownerShare: number | null; status: BookingStatus; message: string; reason: string; place: Place; payment: Payment;
  requestedAt: string; respondBy: string; confirmedAt: string | null; isOwner: boolean; canAccept: boolean; canCancel: boolean; history: BookingEvent[];
  handover: Handover; claim: Claim | null; photos: HandoverPhoto[]; reviews: { canReview: boolean; mine: Review | null; theirs: Review | null };
}

export interface BookingSummary {
  id: string; listing: BookingListing; other: { id: string; name: string }; start: string; end: string; days: number; rent: number;
  status: BookingStatus; isOwner: boolean; requestedAt: string; respondBy: string;
}

export interface LedgerLine { entryId: string; kind: string; bookingId: string | null; memo: string; amount: number; postedAt: string }
export interface Earnings { owed: number; earned: number; refunded: number; lines: LedgerLine[] }
export interface Books {
  balanced: boolean; entries: number; unbalancedEntries: number; ledgerTotal: number; providerClearing: number; providerSays: number; reconciled: boolean;
  accounts: { account: string; balance: number }[];
}
export interface Dispute {
  bookingId: string; listingTitle: string; owner: Party; renter: Party; deposit: number; claim: number; note: string; claimedAt: string;
  answer: "Accepted" | "Rejected" | null; answerNote: string; photos: number;
}

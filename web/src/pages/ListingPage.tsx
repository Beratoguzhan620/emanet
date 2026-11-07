import { CalendarClock, ImageOff, MapPin, Pencil, ShieldCheck, Zap } from "lucide-react";
import { useEffect, useMemo, useState, type FormEvent } from "react";
import { api, ApiError } from "../api";
import { Rating } from "../components/Badges";
import { Calendar } from "../components/Calendar";
import { MapView } from "../components/MapView";
import { Empty, Failed, Loading } from "../components/States";
import { useMaybeAccount } from "../context";
import { useLoad } from "../hooks";
import { categoryLabel, daysBetween, formatDayTime, formatMoney, formatRange, paymentMethods, plural, rentFor, today } from "../format";
import { linkProps, navigate, signInHere } from "../router";
import type { Listing, Quote } from "../types";

function Gallery({ listing }: { listing: Listing }) {
  const [index, setIndex] = useState(0);
  const photo = listing.photos[index];
  if (!photo) return <div className="gallery empty"><ImageOff size={28} aria-hidden="true" /><span className="small muted">No photos yet</span></div>;
  return (
    <div className="gallery">
      <img className="gallery-main" src={photo.url} alt={`${listing.title}, photo ${index + 1} of ${listing.photos.length}`} width={photo.width} height={photo.height} />
      {listing.photos.length > 1 && (
        <div className="thumbs">
          {listing.photos.map((p, i) => (
            <button key={p.id} className={`thumb${i === index ? " on" : ""}`} onClick={() => setIndex(i)} aria-label={`Photo ${i + 1}`} aria-pressed={i === index}>
              <img src={p.thumbUrl} alt="" loading="lazy" />
            </button>
          ))}
        </div>
      )}
    </div>
  );
}

function BookingPanel({ listing, start, end, onDates }: { listing: Listing; start?: string; end?: string; onDates: (start?: string, end?: string) => void }) {
  const account = useMaybeAccount();
  const [quote, setQuote] = useState<Quote | null>(null);
  const [quoteError, setQuoteError] = useState<string | null>(null);
  const [message, setMessage] = useState("");
  const [method, setMethod] = useState<string>(paymentMethods[0].token);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    setQuote(null);
    setQuoteError(null);
    if (!start || !end) return;
    let live = true;
    api.quote(listing.id, start, end).then((q) => live && setQuote(q), (e: Error) => live && setQuoteError(e.message));
    return () => { live = false; };
  }, [listing.id, start, end]);

  async function submit(e: FormEvent) {
    e.preventDefault();
    if (!account) { navigate(signInHere()); return; }
    if (!start || !end) return;
    setBusy(true);
    setError(null);
    try {
      const booking = await api.book({ listingId: listing.id, start, end, message, paymentMethod: method });
      navigate(`/rentals/${booking.id}`);
    } catch (err) {
      const api = err as ApiError;
      setError(api.code === "unavailable" ? "Someone else just took some of these days. Pick others." : api.message);
      setBusy(false);
    }
  }

  const days = start && end ? daysBetween(start, end) : 0;
  const estimate = days > 0 ? rentFor(listing.pricePerDay, listing.weeklyPercentOff, days) : 0;

  return (
    <form className="card booking-panel stack" onSubmit={submit}>
      <div className="row between">
        <span><strong className="price">{formatMoney(listing.pricePerDay)}</strong><span className="muted"> / day</span></span>
        {listing.instantBook ? <span className="badge accent"><Zap size={12} />Instant</span> : <span className="badge plain">Owner answers within a day</span>}
      </div>
      {listing.weeklyPercentOff > 0 && <p className="small muted">{listing.weeklyPercentOff}% off for a week or longer.</p>}
      <div className="grid-2">
        <label className="field">Pickup<input className="input" type="date" min={today()} value={start ?? ""} onChange={(e) => onDates(e.target.value || undefined, end && e.target.value && end > e.target.value ? end : undefined)} required /></label>
        <label className="field">Return<input className="input" type="date" min={start ?? today(1)} value={end ?? ""} onChange={(e) => onDates(start, e.target.value || undefined)} required /></label>
      </div>
      {start && end && (
        <dl className="sums">
          {quote ? (
            <>
              <div><dt>{plural(quote.days, "day")}, {formatRange(quote.start, quote.end)}</dt><dd>{formatMoney(quote.fullPrice)}</dd></div>
              {quote.discount > 0 && <div><dt>Weekly discount</dt><dd className="ok-text">−{formatMoney(quote.discount)}</dd></div>}
              <div className="total"><dt>Rent</dt><dd>{formatMoney(quote.rent)}</dd></div>
              <div><dt><ShieldCheck size={14} aria-hidden="true" /> Deposit, held and let go after the return</dt><dd>{formatMoney(quote.deposit)}</dd></div>
            </>
          ) : quoteError ? null : <div><dt>{plural(days, "day")}</dt><dd className="muted">{formatMoney(estimate)}…</dd></div>}
        </dl>
      )}
      {quoteError && <p className="form-error" role="alert">{quoteError}</p>}
      {quote && !quote.available && <p className="form-error" role="alert">{quote.reason}</p>}
      {account && !listing.isMine && (
        <>
          <label className="field">A note for {listing.owner.name}<textarea className="textarea" rows={2} maxLength={500} value={message} onChange={(e) => setMessage(e.target.value)} placeholder="What you need it for, when you would pick it up" /></label>
          <label className="field">Pay with
            <select className="select" value={method} onChange={(e) => setMethod(e.target.value)}>
              {paymentMethods.map((m) => <option key={m.token} value={m.token}>{m.label}</option>)}
            </select>
            <span className="hint">A stand-in payment provider: nothing is charged and no card number is ever asked for.</span>
          </label>
        </>
      )}
      {error && <p className="form-error" role="alert">{error}</p>}
      {listing.isMine ? (
        <a className="button wide" {...linkProps(`/my/listings/${listing.id}`)}><Pencil size={15} />Edit your listing</a>
      ) : (
        <button className="button primary wide" disabled={busy || !quote?.available}>
          {!account ? "Sign in to rent" : busy ? "Holding the money…" : listing.instantBook ? "Book now" : "Ask to rent"}
        </button>
      )}
      <p className="small muted">The rent and the deposit are held on the card when you ask. The rent is charged once the owner accepts; free cancellation up to two days before pickup.</p>
    </form>
  );
}

export function ListingPage({ listingId, params }: { listingId: string; params: URLSearchParams }) {
  const [listing, reload] = useLoad(() => api.listing(listingId), [listingId]);
  const [calendar] = useLoad(() => api.calendar(listingId), [listingId]);
  const [reviews] = useLoad(() => api.reviews(listingId), [listingId]);
  const start = params.get("start") ?? undefined;
  const end = params.get("end") ?? undefined;
  const setDates = (s?: string, e?: string) => navigate(`/l/${listingId}${s ? `?start=${s}${e ? `&end=${e}` : ""}` : ""}`, true);

  const place = listing.data?.place;
  const map = useMemo(() => place && (place.exact
    ? { pins: [{ id: "here", latitude: place.latitude, longitude: place.longitude, label: place.area, text: "", tone: "accent" as const, shape: "square" as const }], circles: [] }
    : { pins: [], circles: [{ latitude: place.latitude, longitude: place.longitude, radiusMeters: 400, tone: "accent" as const }] }), [place]);

  if (listing.state === "failed" && !listing.data) {
    return (
      <div className="stack">
        <Failed message={listing.error} onRetry={reload} />
        <Empty title="Looking for something else?"><a className="button" {...linkProps("/")}>Back to the search</a></Empty>
      </div>
    );
  }
  if (!listing.data) return <Loading />;
  const l = listing.data;

  return (
    <div className="listing-page">
      <a className="back small" {...linkProps("/")}>← All listings</a>
      <div className="listing-grid">
        <div className="stack-lg min0">
          <Gallery listing={l} />
          <header className="stack-8">
            <span className="small muted">{categoryLabel[l.category]}{l.status !== "Active" ? " · not taking requests" : ""}</span>
            <h1>{l.title}</h1>
            <div className="row wrap gap-10 small">
              <Rating value={l.rating} count={l.ratingCount} />
              <span className="row gap-4 muted"><MapPin size={14} aria-hidden="true" />{l.place.area}</span>
              <span className="row gap-4 muted"><CalendarClock size={14} aria-hidden="true" />{l.minDays === l.maxDays ? plural(l.minDays, "day") : `${l.minDays}–${l.maxDays} days`}</span>
            </div>
          </header>
          {l.description && <p className="pre">{l.description}</p>}
          <section className="card stack" aria-label="Availability">
            <h2>When it is free</h2>
            {calendar.state === "failed" ? <Failed message={calendar.error} /> : calendar.data ? (
              <Calendar ranges={calendar.data} start={start} end={end} onPick={l.isMine ? undefined : (s, e) => setDates(s, e)} owner={l.isMine} />
            ) : <Loading label="Loading the calendar" />}
          </section>
          <section className="card stack" aria-label="Where">
            <div className="row between"><h2>Where</h2><span className="small muted">{l.place.exact ? "The exact place" : "Within about 400 m"}</span></div>
            {map && <MapView pins={map.pins} circles={map.circles} height={260} fitKey={l.id} />}
          </section>
          <section className="stack" aria-label="Reviews">
            <h2>What renters said</h2>
            {reviews.state === "failed" && <Failed message={reviews.error} />}
            {reviews.data && reviews.data.items.length === 0 && <p className="muted">No reviews yet.</p>}
            {reviews.data?.items.map((r) => (
              <article key={r.id} className="review">
                <div className="row between"><strong>{r.authorName}</strong><span className="small muted">{"★".repeat(r.stars)}{"☆".repeat(5 - r.stars)} · {formatDayTime(r.at)}</span></div>
                {r.comment && <p>{r.comment}</p>}
              </article>
            ))}
          </section>
        </div>
        <aside className="listing-side">
          <BookingPanel listing={l} start={start} end={end} onDates={setDates} />
          <div className="card owner-card">
            <span className="avatar">{l.owner.name.slice(0, 1).toLocaleUpperCase("tr-TR")}</span>
            <div className="min0">
              <strong className="block">{l.owner.name}</strong>
              <span className="small muted">Member since {new Date(l.owner.memberSince).getFullYear()} · {plural(l.owner.activeListings, "listing")}</span>
            </div>
          </div>
        </aside>
      </div>
    </div>
  );
}

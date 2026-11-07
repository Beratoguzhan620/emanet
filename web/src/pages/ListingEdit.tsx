import { ArrowLeft, ArrowRight, Eye, Loader2, Pause, Play, Trash2, Upload } from "lucide-react";
import { useRef, useState, type FormEvent } from "react";
import { api, ApiError } from "../api";
import { Calendar } from "../components/Calendar";
import { Dialog } from "../components/Dialog";
import { MapView } from "../components/MapView";
import { Failed, Loading } from "../components/States";
import { useLoad } from "../hooks";
import { categories, categoryLabel, formatRange, today } from "../format";
import { linkProps, navigate } from "../router";
import type { Listing, ListingInput } from "../types";

const blank: ListingInput = {
  title: "", description: "", category: "Tools", pricePerDay: 150, weeklyPercentOff: 20, deposit: 1000,
  latitude: 41.2867, longitude: 36.33, area: "", minDays: 1, maxDays: 14, instantBook: false,
};

const inputOf = (l: Listing): ListingInput => ({
  title: l.title, description: l.description, category: l.category, pricePerDay: l.pricePerDay, weeklyPercentOff: l.weeklyPercentOff, deposit: l.deposit,
  latitude: l.place.latitude, longitude: l.place.longitude, area: l.place.area, minDays: l.minDays, maxDays: l.maxDays, instantBook: l.instantBook,
});

function Details({ listing, onSaved }: { listing?: Listing; onSaved: (l: Listing) => void }) {
  const [form, setForm] = useState<ListingInput>(listing ? inputOf(listing) : blank);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<ApiError | null>(null);
  const [saved, setSaved] = useState(false);
  const set = <K extends keyof ListingInput>(key: K, value: ListingInput[K]) => { setForm({ ...form, [key]: value }); setSaved(false); };
  const num = (key: "pricePerDay" | "weeklyPercentOff" | "deposit" | "minDays" | "maxDays") => (e: React.ChangeEvent<HTMLInputElement>) => set(key, Number(e.target.value));
  const fieldError = (name: string) => error?.fields[name]?.[0];

  async function submit(e: FormEvent) {
    e.preventDefault();
    setBusy(true);
    setError(null);
    try {
      const result = listing ? await api.updateListing(listing.id, form) : await api.createListing(form);
      setSaved(true);
      onSaved(result);
    } catch (err) {
      setError(err as ApiError);
    } finally {
      setBusy(false);
    }
  }

  return (
    <form className="card stack" onSubmit={submit} noValidate>
      <h2>{listing ? "Details" : "What are you renting out?"}</h2>
      <label className="field">Title<input className="input" value={form.title} onChange={(e) => set("title", e.target.value)} maxLength={80} placeholder="Darbeli matkap, Bosch" required />
        {fieldError("title") && <span className="field-error">{fieldError("title")}</span>}</label>
      <div className="grid-2">
        <label className="field">Category
          <select className="select" value={form.category} onChange={(e) => set("category", e.target.value as ListingInput["category"])}>
            {categories.map((c) => <option key={c} value={c}>{categoryLabel[c]}</option>)}
          </select>
        </label>
        <label className="field">Neighbourhood<input className="input" value={form.area} onChange={(e) => set("area", e.target.value)} maxLength={80} placeholder="Atakum, Samsun" />
          <span className="hint">Shown to everyone; the exact place only to confirmed renters.</span>
          {fieldError("area") && <span className="field-error">{fieldError("area")}</span>}</label>
      </div>
      <label className="field">Description<textarea className="textarea" rows={4} maxLength={2000} value={form.description} onChange={(e) => set("description", e.target.value)} placeholder="What comes with it, what it is good for, anything to know" /></label>
      <div className="grid-3">
        <label className="field">Price a day (₺)<input className="input" type="number" min={1} step="0.01" value={form.pricePerDay} onChange={num("pricePerDay")} />
          {fieldError("pricePerDay") && <span className="field-error">{fieldError("pricePerDay")}</span>}</label>
        <label className="field">Off for a week (%)<input className="input" type="number" min={0} max={50} value={form.weeklyPercentOff} onChange={num("weeklyPercentOff")} /></label>
        <label className="field">Deposit (₺)<input className="input" type="number" min={0} step="0.01" value={form.deposit} onChange={num("deposit")} />
          {fieldError("deposit") && <span className="field-error">{fieldError("deposit")}</span>}</label>
      </div>
      <div className="grid-3">
        <label className="field">Shortest rental (days)<input className="input" type="number" min={1} max={90} value={form.minDays} onChange={num("minDays")} /></label>
        <label className="field">Longest rental (days)<input className="input" type="number" min={1} max={90} value={form.maxDays} onChange={num("maxDays")} />
          {fieldError("maxDays") && <span className="field-error">{fieldError("maxDays")}</span>}</label>
        <label className="check align-end"><input type="checkbox" checked={form.instantBook} onChange={(e) => set("instantBook", e.target.checked)} /><span>Instant booking<span className="hint block">Requests are accepted at once.</span></span></label>
      </div>
      <div className="field">
        <span>Where it is picked up <span className="small muted">· click the map</span></span>
        <MapView pins={[{ id: "here", latitude: form.latitude, longitude: form.longitude, label: "Pickup place", text: "", tone: "accent", shape: "square" }]}
          height={260} fitKey={listing?.id ?? "new"} onPick={(lat, lng) => { setForm((f) => ({ ...f, latitude: lat, longitude: lng })); setSaved(false); }} />
        {(fieldError("latitude") || fieldError("longitude")) && <span className="field-error">{fieldError("latitude") ?? fieldError("longitude")}</span>}
      </div>
      {error && Object.keys(error.fields).length === 0 && <p className="form-error" role="alert">{error.message}</p>}
      <div className="row gap-6">
        <button className="button primary" disabled={busy}>{busy ? "Saving…" : listing ? "Save changes" : "Create the listing"}</button>
        {saved && <span className="small ok-text" role="status">Saved</span>}
      </div>
    </form>
  );
}

function Photos({ listing, onChange }: { listing: Listing; onChange: (l: Listing) => void }) {
  const input = useRef<HTMLInputElement>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function run(work: () => Promise<unknown>) {
    setBusy(true);
    setError(null);
    try {
      await work();
      onChange(await api.listing(listing.id));
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setBusy(false);
    }
  }

  const move = (index: number, by: number) => {
    const ids = listing.photos.map((p) => p.id);
    const [moved] = ids.splice(index, 1);
    ids.splice(index + by, 0, moved!);
    void run(() => api.orderPhotos(listing.id, ids));
  };

  return (
    <section className="card stack" aria-label="Photos">
      <div className="row between">
        <h2>Photos <span className="small muted">{listing.photos.length} of 8 · the first is the cover</span></h2>
        <button className="button small" onClick={() => input.current?.click()} disabled={busy || listing.photos.length >= 8}>
          {busy ? <Loader2 size={14} className="spin" /> : <Upload size={14} />}Add photos
        </button>
      </div>
      <input ref={input} type="file" accept="image/jpeg,image/png,image/webp" multiple hidden
        onChange={(e) => { const files = Array.from(e.target.files ?? []); e.target.value = ""; void run(async () => { for (const f of files.slice(0, 8 - listing.photos.length)) await api.addPhoto(listing.id, f); }); }} />
      {error && <p className="form-error" role="alert">{error}</p>}
      {listing.photos.length === 0 ? <p className="small muted">Listings with photos are rented far more. They are turned upright and their location data is removed.</p> : (
        <div className="photo-grid">
          {listing.photos.map((p, i) => (
            <figure key={p.id} className="photo-tile editable">
              <img src={p.thumbUrl} alt={`Photo ${i + 1}`} />
              <figcaption className="row between">
                <button className="icon-button" onClick={() => move(i, -1)} disabled={busy || i === 0} aria-label="Move earlier"><ArrowLeft size={14} /></button>
                <button className="icon-button" onClick={() => void run(() => api.removePhoto(listing.id, p.id))} disabled={busy} aria-label="Remove photo"><Trash2 size={14} /></button>
                <button className="icon-button" onClick={() => move(i, 1)} disabled={busy || i === listing.photos.length - 1} aria-label="Move later"><ArrowRight size={14} /></button>
              </figcaption>
            </figure>
          ))}
        </div>
      )}
    </section>
  );
}

function Availability({ listing }: { listing: Listing }) {
  const [ranges, reload] = useLoad(() => api.calendar(listing.id), [listing.id]);
  const [pick, setPick] = useState<{ start?: string; end?: string }>({});
  const [note, setNote] = useState("");
  const [error, setError] = useState<string | null>(null);

  async function close() {
    if (!pick.start || !pick.end) return;
    setError(null);
    try {
      await api.block(listing.id, pick.start, pick.end, note);
      setPick({});
      setNote("");
      reload();
    } catch (e) {
      setError((e as Error).message);
    }
  }

  const closed = (ranges.data ?? []).filter((r) => r.kind === "Closed" && r.end > today());
  return (
    <section className="card stack" aria-label="Calendar">
      <h2>Calendar</h2>
      <p className="small muted">Close days when the item is not available. Rented days are filled in for you.</p>
      {ranges.state === "failed" && <Failed message={ranges.error} onRetry={reload} />}
      {ranges.data && <Calendar ranges={ranges.data} start={pick.start} end={pick.end} onPick={(start, end) => setPick({ start, end })} owner />}
      {pick.start && pick.end && (
        <div className="row gap-6 wrap">
          <input className="input grow" placeholder="Note for yourself (optional)" value={note} onChange={(e) => setNote(e.target.value)} maxLength={200} />
          <button className="button" onClick={() => void close()}>Close {formatRange(pick.start, pick.end)}</button>
        </div>
      )}
      {error && <p className="form-error" role="alert">{error}</p>}
      {closed.length > 0 && (
        <ul className="plain-list">
          {closed.map((r) => (
            <li key={r.id} className="row between">
              <span>{formatRange(r.start, r.end)}{r.note ? <span className="muted"> · {r.note}</span> : null}</span>
              <button className="button small" onClick={() => void api.unblock(listing.id, r.id!).then(reload, (e: Error) => setError(e.message))}>Open again</button>
            </li>
          ))}
        </ul>
      )}
    </section>
  );
}

export function ListingEdit({ listingId }: { listingId?: string }) {
  const [listing, reload, set] = useLoad(() => (listingId ? api.listing(listingId) : Promise.resolve(undefined)), [listingId]);
  const [confirm, setConfirm] = useState(false);
  const [error, setError] = useState<string | null>(null);

  if (listing.state === "failed" && !listing.data) return <Failed message={listing.error} onRetry={reload} />;
  if (listingId && !listing.data) return <Loading />;
  const l = listing.data;

  const status = async (work: () => Promise<Listing>) => {
    setError(null);
    try { set(await work()); } catch (e) { setError((e as Error).message); }
  };

  return (
    <div className="stack-lg narrow-page">
      <a className="back small" {...linkProps("/my/listings")}>← My listings</a>
      <div className="page-head">
        <h1>{l ? l.title : "List something"}</h1>
        {l && (
          <div className="row gap-6 wrap">
            <a className="button small" {...linkProps(`/l/${l.id}`)}><Eye size={14} />See it as others do</a>
            {l.status === "Active"
              ? <button className="button small" onClick={() => void status(() => api.pause(l.id))}><Pause size={14} />Pause</button>
              : <button className="button small" onClick={() => void status(() => api.resume(l.id))}><Play size={14} />List again</button>}
            <button className="button small danger" onClick={() => setConfirm(true)}><Trash2 size={14} />Take down</button>
          </div>
        )}
      </div>
      {l && l.status === "Paused" && <p className="notice">Paused: it does not show in search and nobody can ask for it. Rentals already agreed go ahead.</p>}
      {error && <p className="form-error" role="alert">{error}</p>}
      <Details key={l?.id ?? "new"} listing={l} onSaved={(saved) => (l ? set(saved) : navigate(`/my/listings/${saved.id}`, true))} />
      {l && <Photos listing={l} onChange={set} />}
      {l && <Availability listing={l} />}
      {confirm && l && (
        <Dialog title="Take the listing down?" onClose={() => setConfirm(false)}>
          <div className="stack">
            <p>It leaves search for good. Rentals already agreed still go ahead.</p>
            <div className="dialog-actions">
              <button className="button" onClick={() => setConfirm(false)}>Keep it</button>
              <button className="button danger" onClick={() => void api.removeListing(l.id).then(() => navigate("/my/listings", true), (e: Error) => { setConfirm(false); setError(e.message); })}>Take it down</button>
            </div>
          </div>
        </Dialog>
      )}
    </div>
  );
}

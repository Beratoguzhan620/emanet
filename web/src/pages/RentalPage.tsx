import { Check, KeyRound, MapPin, ShieldAlert, ShieldCheck, X } from "lucide-react";
import { useMemo, useState, type FormEvent, type ReactNode } from "react";
import { api } from "../api";
import { Rating, StarPicker, StatusBadge } from "../components/Badges";
import { ConditionPhotos } from "../components/ConditionPhotos";
import { Dialog } from "../components/Dialog";
import { MapView } from "../components/MapView";
import { Failed, Loading } from "../components/States";
import { useLoad, useNow } from "../hooks";
import { eventLabel, formatDay, formatDayTime, formatMoney, plural, relative } from "../format";
import { linkProps } from "../router";
import type { Booking } from "../types";

type Act = (run: () => Promise<Booking>) => Promise<boolean>;

const steps = [
  { label: "Asked", done: () => true },
  { label: "Confirmed", done: (b: Booking) => !!b.confirmedAt },
  { label: "Picked up", done: (b: Booking) => !!b.handover.pickedUpAt },
  { label: "Returned", done: (b: Booking) => !!b.handover.returnedAt },
  { label: "Settled", done: (b: Booking) => b.status === "Completed" },
];

function Steps({ booking }: { booking: Booking }) {
  if (["Declined", "Expired", "Cancelled"].includes(booking.status)) return null;
  return (
    <ol className="steps" aria-label="Progress">
      {steps.map((s) => <li key={s.label} className={`step${s.done(booking) ? " done" : ""}`}><span className="step-dot" aria-hidden="true" />{s.label}</li>)}
    </ol>
  );
}

function BigCode({ code, children }: { code: string; children: ReactNode }) {
  return (
    <div className="code-show">
      <KeyRound size={18} aria-hidden="true" />
      <span className="code-big" aria-label={`Code ${code.split("").join(" ")}`}>{code.slice(0, 3)} {code.slice(3)}</span>
      <span className="small muted">{children}</span>
    </div>
  );
}

function CodeForm({ label, hint, button, onSubmit }: { label: string; hint: string; button: string; onSubmit: (code: string) => Promise<boolean> }) {
  const [code, setCode] = useState("");
  const [busy, setBusy] = useState(false);
  async function submit(e: FormEvent) {
    e.preventDefault();
    setBusy(true);
    if (await onSubmit(code.replace(/\s/g, ""))) setCode("");
    setBusy(false);
  }
  return (
    <form className="code-form" onSubmit={submit}>
      <label className="field">{label}
        <input className="input code-input" inputMode="numeric" autoComplete="one-time-code" pattern="[0-9 ]{6,7}" maxLength={7} value={code} onChange={(e) => setCode(e.target.value)} required />
        <span className="hint">{hint}</span>
      </label>
      <button className="button primary" disabled={busy || code.replace(/\s/g, "").length !== 6}>{busy ? "Checking…" : button}</button>
    </form>
  );
}

function ReasonDialog({ title, label, button, danger, note, onClose, onSubmit }: {
  title: string; label: string; button: string; danger?: boolean; note?: ReactNode; onClose: () => void; onSubmit: (reason: string) => Promise<boolean>;
}) {
  const [reason, setReason] = useState("");
  const [busy, setBusy] = useState(false);
  async function submit(e: FormEvent) {
    e.preventDefault();
    setBusy(true);
    if (await onSubmit(reason)) onClose();
    else setBusy(false);
  }
  return (
    <Dialog title={title} onClose={onClose}>
      <form className="stack" onSubmit={submit}>
        {note}
        <label className="field">{label}<textarea className="textarea" rows={3} maxLength={300} value={reason} onChange={(e) => setReason(e.target.value)} /></label>
        <div className="dialog-actions">
          <button type="button" className="button" onClick={onClose}>Back</button>
          <button className={`button ${danger ? "danger" : "primary"}`} disabled={busy}>{busy ? "Saving…" : button}</button>
        </div>
      </form>
    </Dialog>
  );
}

function ClaimForm({ booking, act }: { booking: Booking; act: Act }) {
  const [open, setOpen] = useState(false);
  const [amount, setAmount] = useState("");
  const [note, setNote] = useState("");
  async function submit(e: FormEvent) {
    e.preventDefault();
    if (await act(() => api.claim(booking.id, Number(amount), note))) setOpen(false);
  }
  if (!open) return <button className="button danger" onClick={() => setOpen(true)}><ShieldAlert size={15} />Report damage</button>;
  return (
    <form className="stack card inset" onSubmit={submit}>
      <h3>Report damage</h3>
      <p className="small muted">Add damage photos above first. {booking.renter.name} can agree, or ask support to decide. Up to the deposit of {formatMoney(booking.deposit)}.</p>
      <label className="field">What it costs to fix (₺)<input className="input small-input" type="number" min={1} max={booking.deposit} step="0.01" value={amount} onChange={(e) => setAmount(e.target.value)} required /></label>
      <label className="field">What happened<textarea className="textarea" rows={3} minLength={10} maxLength={1000} value={note} onChange={(e) => setNote(e.target.value)} required /></label>
      <div className="row gap-6"><button type="button" className="button" onClick={() => setOpen(false)}>Back</button><button className="button danger">Send the claim</button></div>
    </form>
  );
}

function ReviewForm({ booking, onDone }: { booking: Booking; onDone: () => void }) {
  const [stars, setStars] = useState(0);
  const [comment, setComment] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const about = booking.isOwner ? booking.renter.name : `${booking.listing.title} and ${booking.owner.name}`;
  async function submit(e: FormEvent) {
    e.preventDefault();
    setBusy(true);
    setError(null);
    try {
      await api.review(booking.id, stars, comment);
      onDone();
    } catch (err) {
      setError((err as Error).message);
      setBusy(false);
    }
  }
  return (
    <form className="card stack" onSubmit={submit}>
      <h2>How was it?</h2>
      <p className="small muted">A review of {about}. You can write it once, within two weeks.</p>
      <StarPicker value={stars} onChange={setStars} />
      <label className="field">A few words, if you like<textarea className="textarea" rows={3} maxLength={500} value={comment} onChange={(e) => setComment(e.target.value)} /></label>
      {error && <p className="form-error" role="alert">{error}</p>}
      <button className="button primary" disabled={busy || stars === 0}>{busy ? "Saving…" : "Post the review"}</button>
    </form>
  );
}

/** What this person can do now, and what they are waiting for. */
function Next({ b, act, now }: { b: Booking; act: Act; now: number }) {
  const [dialog, setDialog] = useState<"decline" | "cancel" | "reject" | null>(null);
  const other = b.isOwner ? b.renter.name : b.owner.name;
  let body: ReactNode = null;

  if (b.status === "Requested") {
    body = b.isOwner ? (
      <>
        <p>{b.renter.name} would like it for {plural(b.days, "day")}. Answer {relative(b.respondBy, now)}, or the request lapses and the holds are let go.</p>
        {b.message && <blockquote className="quote-text">{b.message}</blockquote>}
        <div className="row gap-6 wrap">
          <button className="button primary" onClick={() => void act(() => api.accept(b.id))} disabled={!b.canAccept}><Check size={15} />Accept and charge the rent</button>
          <button className="button danger" onClick={() => setDialog("decline")}><X size={15} />Decline</button>
        </div>
      </>
    ) : <p>{b.owner.name} answers {relative(b.respondBy, now)}. The rent and deposit are only held on your card for now.</p>;
  } else if (b.status === "Confirmed") {
    body = b.isOwner ? (
      b.handover.canPickUp
        ? <CodeForm label={`The code on ${b.renter.name}'s screen`} hint="Type it in when you hand the item over." button="Handed over" onSubmit={(code) => act(() => api.pickUp(b.id, code))} />
        : <p>Pickup is from {formatDay(b.start)}. {b.renter.name} will show you a code then.</p>
    ) : b.handover.pickupCode ? <BigCode code={b.handover.pickupCode}>Show this to {b.owner.name} when you pick it up, from {formatDay(b.start)}.</BigCode> : null;
  } else if (b.status === "Active") {
    body = b.isOwner
      ? b.handover.returnCode && <BigCode code={b.handover.returnCode}>Show this to {b.renter.name} when the item comes back, by {formatDay(b.end)}.</BigCode>
      : <CodeForm label={`The code on ${b.owner.name}'s screen`} hint={`Bring it back by ${formatDay(b.end)} and type in the code at the handover.`} button="Given back" onSubmit={(code) => act(() => api.giveBack(b.id, code))} />;
  } else if (b.status === "Returned") {
    body = b.isOwner ? (
      <>
        <p>Check the item. Report damage {b.handover.claimBy ? relative(b.handover.claimBy, now) : "soon"}, or the deposit is let go on its own.</p>
        <div className="row gap-6 wrap">
          {b.handover.canSignOff && <button className="button primary" onClick={() => void act(() => api.allGood(b.id))}><ShieldCheck size={15} />All good, let the deposit go</button>}
          {b.handover.canReportDamage && <ClaimForm booking={b} act={act} />}
        </div>
      </>
    ) : <p>{b.owner.name} can report damage {b.handover.claimBy ? relative(b.handover.claimBy, now) : "for two days"}. If not, your deposit is let go.</p>;
  } else if (b.status === "Disputed" && b.claim) {
    const c = b.claim;
    body = (
      <>
        <div className="claim-box">
          <strong>{b.isOwner ? "You asked for" : `${b.owner.name} asks for`} {formatMoney(c.amount)} of the {formatMoney(b.deposit)} deposit</strong>
          <p className="pre">{c.note}</p>
        </div>
        {c.answer === "Rejected" && <p>{b.isOwner ? `${b.renter.name} disagrees` : "You disagreed"}{c.answerNote ? `: “${c.answerNote}”` : ""}. Support will look at the photos and decide.</p>}
        {c.answer === null && (c.canAnswer ? (
          <>
            <p>Answer {relative(c.answerBy, now)}. If you do not, the claim is taken as accepted.</p>
            <div className="row gap-6 wrap">
              <button className="button" onClick={() => void act(() => api.acceptClaim(b.id))}>Agree, take {formatMoney(c.amount)}</button>
              <button className="button danger" onClick={() => setDialog("reject")}>Disagree, ask support</button>
            </div>
          </>
        ) : <p>{b.renter.name} has until {formatDayTime(c.answerBy)} to answer.</p>)}
      </>
    );
  } else if (b.status === "Completed") {
    body = (
      <p>
        {b.payment.depositCharged > 0 ? `${formatMoney(b.payment.depositCharged)} of the deposit went to ${b.owner.name} for damage; the rest was let go.` : "The deposit was let go in full."}
        {b.reason ? ` ${b.reason}` : ""}
      </p>
    );
  } else {
    body = <p>{b.reason || "This rental is closed."} {b.payment.refund > 0 ? `${formatMoney(b.payment.refund)} goes back to the card.` : "Nothing was charged; the holds were let go."}</p>;
  }

  return (
    <section className="card stack next" aria-label="What happens next">
      {body}
      {b.canCancel && <button className="link small align-start" onClick={() => setDialog("cancel")}>Cancel the rental</button>}
      {dialog === "decline" && <ReasonDialog title="Decline the request" label={`A word for ${other} (optional)`} button="Decline" danger onClose={() => setDialog(null)} onSubmit={(r) => act(() => api.decline(b.id, r))} />}
      {dialog === "reject" && <ReasonDialog title="Ask support to decide" label="Your side, briefly" button="Send to support" danger onClose={() => setDialog(null)} onSubmit={(r) => act(() => api.rejectClaim(b.id, r))} />}
      {dialog === "cancel" && (
        <ReasonDialog title="Cancel the rental" label="Why (optional)" button="Cancel the rental" danger onClose={() => setDialog(null)} onSubmit={(r) => act(() => api.cancel(b.id, r))}
          note={<p className="small muted">{b.status === "Requested" ? "Nothing has been charged; the holds are let go." : b.isOwner ? `${b.renter.name} gets the whole rent back.` : "The whole rent comes back up to two days before pickup; half after that. The deposit hold is let go."}</p>} />
      )}
    </section>
  );
}

export function RentalView({ booking, set, reload, admin = false }: { booking: Booking; set: (b: Booking) => void; reload: () => void; admin?: boolean }) {
  const now = useNow(30_000);
  const [error, setError] = useState<string | null>(null);
  const b = booking;
  const act: Act = async (run) => {
    setError(null);
    try {
      set(await run());
      return true;
    } catch (e) {
      setError((e as Error).message);
      return false;
    }
  };
  const map = useMemo(() => b.place.exact
    ? { pins: [{ id: "p", latitude: b.place.latitude, longitude: b.place.longitude, label: b.place.area, text: "", tone: "accent" as const, shape: "square" as const }], circles: [] }
    : { pins: [], circles: [{ latitude: b.place.latitude, longitude: b.place.longitude, radiusMeters: 400, tone: "accent" as const }] }, [b.place]);

  return (
    <div className="rental-page">
      <a className="back small" {...linkProps(admin ? "/admin" : `/rentals${b.isOwner ? "?as=owner" : ""}`)}>← {admin ? "Claims" : "Rentals"}</a>
      <header className="page-head">
        <div className="min0 stack-8">
          <div className="row gap-10 wrap"><h1 className="min0"><a className="strong-link" {...linkProps(`/l/${b.listing.id}`)}>{b.listing.title}</a></h1><StatusBadge status={b.status} /></div>
          <span className="muted">{formatDay(b.start)} → {formatDay(b.end)} · {plural(b.days, "day")}</span>
        </div>
      </header>
      <Steps booking={b} />
      {error && <p className="form-error" role="alert">{error}</p>}
      <div className="rental-grid">
        <div className="stack-lg min0">
          {!admin && <Next b={b} act={act} now={now} />}
          {admin && b.claim && (
            <section className="card stack">
              <div className="claim-box"><strong>{b.owner.name} asks for {formatMoney(b.claim.amount)} of {formatMoney(b.deposit)}</strong><p className="pre">{b.claim.note}</p></div>
              {b.claim.answer === "Rejected" && <p><strong>{b.renter.name}:</strong> {b.claim.answerNote || "No reason given."}</p>}
            </section>
          )}
          <ConditionPhotos booking={b} readOnly={admin} onAdded={(photo) => set({ ...b, photos: [...b.photos, photo] })} />
          {!admin && b.reviews.canReview && <ReviewForm booking={b} onDone={reload} />}
          {(b.reviews.mine || b.reviews.theirs) && (
            <section className="card stack" aria-label="Reviews">
              <h2>Reviews</h2>
              {[b.reviews.mine, b.reviews.theirs].filter((r) => r !== null).map((r) => (
                <article key={r!.id} className="review"><div className="row between"><strong>{r!.authorName}</strong><span className="small muted">{"★".repeat(r!.stars)}{"☆".repeat(5 - r!.stars)}</span></div>{r!.comment && <p>{r!.comment}</p>}</article>
              ))}
            </section>
          )}
          <section className="card stack" aria-label="History">
            <h2>History</h2>
            <ol className="history">
              {b.history.map((e) => (
                <li key={e.id}><span className="small muted nowrap">{formatDayTime(e.at)}</span><span><strong>{eventLabel[e.kind]}</strong>{e.detail ? ` · ${e.detail}` : ""}{e.actorName ? <span className="muted"> · {e.actorName}</span> : null}</span></li>
              ))}
            </ol>
          </section>
        </div>
        <aside className="stack-lg">
          <section className="card stack-8" aria-label="People">
            <div className="row between"><span className="small muted">Owner</span><span>{b.owner.name}</span></div>
            <div className="row between"><span className="small muted">Renter</span><span className="row gap-6">{b.renter.name}<Rating value={b.renter.rating} count={b.renter.ratingCount} /></span></div>
          </section>
          <section className="card stack-8" aria-label="Money">
            <dl className="sums">
              <div><dt>Rent</dt><dd>{formatMoney(b.rent)}</dd></div>
              {b.fee !== null && <div><dt>Service fee (10%)</dt><dd>−{formatMoney(b.fee)}</dd></div>}
              {b.ownerShare !== null && <div className="total"><dt>{admin ? "Owner's share" : "Yours"}</dt><dd>{formatMoney(b.ownerShare)}</dd></div>}
              <div><dt>Deposit</dt><dd>{formatMoney(b.deposit)}</dd></div>
            </dl>
            <ul className="money-facts small">
              <li className={b.payment.rentCharged ? "ok-text" : "muted"}>{b.payment.rentCharged ? "Rent charged" : b.confirmedAt ? "Rent being charged" : "Rent held, not charged"}</li>
              {b.payment.refund > 0 && <li className={b.payment.refunded ? "ok-text" : "muted"}>{formatMoney(b.payment.refund)} {b.payment.refunded ? "refunded" : "to refund"}</li>}
              {b.payment.depositSettled
                ? <li className="ok-text">{b.payment.depositCharged > 0 ? `${formatMoney(b.payment.depositCharged)} of the deposit taken, rest let go` : "Deposit let go"}</li>
                : b.payment.holdsReleased ? <li className="ok-text">Holds let go</li> : <li className="muted">Deposit held on the card</li>}
            </ul>
          </section>
          <section className="card stack-8" aria-label="Where">
            <div className="row between"><strong className="row gap-6"><MapPin size={15} aria-hidden="true" />{b.place.area}</strong><span className="small muted">{b.place.exact ? "Exact place" : "About 400 m"}</span></div>
            <MapView pins={map.pins} circles={map.circles} height={200} fitKey={b.id} />
            {!b.place.exact && <p className="small muted">The exact place appears here once the rental is confirmed.</p>}
          </section>
        </aside>
      </div>
    </div>
  );
}

export function RentalPage({ bookingId }: { bookingId: string }) {
  const [booking, reload, set] = useLoad(() => api.booking(bookingId), [bookingId]);
  if (booking.state === "failed" && !booking.data) return <Failed message={booking.error} onRetry={reload} />;
  if (!booking.data) return <Loading />;
  return <RentalView booking={booking.data} set={set} reload={reload} />;
}

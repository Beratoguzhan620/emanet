import { CheckCircle2, Scale, XCircle } from "lucide-react";
import { useState, type FormEvent } from "react";
import { api } from "../api";
import { Empty, Failed, Loading } from "../components/States";
import { useLoad } from "../hooks";
import { formatDayTime, formatMoney, plural } from "../format";
import { linkProps } from "../router";
import { RentalView } from "./RentalPage";

function BooksCheck() {
  const [books, reload] = useLoad(() => api.books(), []);
  if (books.state === "failed") return <Failed message={books.error} onRetry={reload} />;
  if (!books.data) return <Loading label="Checking the books" />;
  const b = books.data;
  const ok = b.balanced && b.reconciled;
  return (
    <section className="card stack" aria-label="The books">
      <div className="row between">
        <h2 className="row gap-6">{ok ? <CheckCircle2 size={18} className="ok-text" /> : <XCircle size={18} className="danger-text" />}The books</h2>
        <button className="button small" onClick={reload}>Check again</button>
      </div>
      <dl className="sums">
        <div><dt>Journal entries</dt><dd>{b.entries}{b.unbalancedEntries > 0 ? `, ${b.unbalancedEntries} not balanced` : ", all balanced"}</dd></div>
        <div><dt>Ledger total</dt><dd>{formatMoney(b.ledgerTotal)}</dd></div>
        <div><dt>Held at the provider, says the ledger</dt><dd>{formatMoney(b.providerClearing)}</dd></div>
        <div><dt>Held at the provider, says the provider</dt><dd className={b.reconciled ? "ok-text" : "danger-text"}>{formatMoney(b.providerSays)}</dd></div>
      </dl>
      <table className="table small">
        <thead><tr><th>Account</th><th className="right">Balance</th></tr></thead>
        <tbody>{b.accounts.map((a) => <tr key={a.account}><td className="mono">{a.account}</td><td className="right mono">{formatMoney(a.balance)}</td></tr>)}</tbody>
      </table>
    </section>
  );
}

export function Admin() {
  const [disputes, reload] = useLoad(() => api.disputes(), []);
  return (
    <div className="stack-lg narrow-page">
      <h1>Claims</h1>
      {disputes.state === "failed" && <Failed message={disputes.error} onRetry={reload} />}
      {disputes.state === "loading" && !disputes.data && <Loading />}
      {disputes.data && disputes.data.length === 0 && <Empty title="No open claims" icon={<Scale size={24} />}><span className="muted">Damage claims the two sides cannot settle come here.</span></Empty>}
      <div className="stack-8">
        {disputes.data?.map((d) => (
          <a key={d.bookingId} className="rental-row card" {...linkProps(`/admin/${d.bookingId}`)}>
            <div className="min0 grow">
              <strong className="ellipsis block">{d.listingTitle}</strong>
              <span className="small muted ellipsis block">{d.owner.name} asks {d.renter.name} for {formatMoney(d.claim)} of {formatMoney(d.deposit)} · {plural(d.photos, "photo")} · {formatDayTime(d.claimedAt)}</span>
            </div>
            <span className={`badge ${d.answer === "Rejected" ? "danger" : "info"}`}>{d.answer === "Rejected" ? "Needs a decision" : "Waiting for the renter"}</span>
          </a>
        ))}
      </div>
      <BooksCheck />
    </div>
  );
}

function Decide({ bookingId, claim, onDone }: { bookingId: string; claim: number; onDone: () => void }) {
  const [amount, setAmount] = useState(String(claim));
  const [note, setNote] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  async function submit(e: FormEvent) {
    e.preventDefault();
    setBusy(true);
    setError(null);
    try {
      await api.resolve(bookingId, Number(amount), note);
      onDone();
    } catch (err) {
      setError((err as Error).message);
      setBusy(false);
    }
  }
  return (
    <form className="card stack" onSubmit={submit}>
      <h2>Decide</h2>
      <label className="field">Take from the deposit (₺)<input className="input small-input" type="number" min={0} max={claim} step="0.01" value={amount} onChange={(e) => setAmount(e.target.value)} required />
        <span className="hint">From nothing to the {formatMoney(claim)} claimed. The rest of the deposit is let go.</span></label>
      <label className="field">Note to both sides<textarea className="textarea" rows={2} maxLength={300} value={note} onChange={(e) => setNote(e.target.value)} /></label>
      {error && <p className="form-error" role="alert">{error}</p>}
      <button className="button primary" disabled={busy}>{busy ? "Saving…" : "Settle the claim"}</button>
    </form>
  );
}

export function AdminRental({ bookingId }: { bookingId: string }) {
  const [booking, reload, set] = useLoad(() => api.adminBooking(bookingId), [bookingId]);
  if (booking.state === "failed" && !booking.data) return <Failed message={booking.error} onRetry={reload} />;
  if (!booking.data) return <Loading />;
  const b = booking.data;
  return (
    <div className="stack-lg">
      <RentalView booking={b} set={set} reload={reload} admin />
      {b.status === "Disputed" && b.claim && <div className="narrow-page"><Decide bookingId={b.id} claim={b.claim.amount} onDone={reload} /></div>}
    </div>
  );
}

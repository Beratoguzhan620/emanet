import { Wallet } from "lucide-react";
import { api } from "../api";
import { Empty, Failed, Loading } from "../components/States";
import { useLoad } from "../hooks";
import { formatDayTime, formatMoney } from "../format";
import { linkProps } from "../router";

const kindLabel: Record<string, string> = { RentCharged: "Rent", RentRefunded: "Refund", DamageCharged: "Damage from the deposit", Payout: "Paid out" };

/** The owner's account in the ledger: every line comes from a balanced entry and none is ever changed. */
export function Earnings() {
  const [earnings, reload] = useLoad(() => api.earnings(), []);
  if (earnings.state === "failed" && !earnings.data) return <Failed message={earnings.error} onRetry={reload} />;
  if (!earnings.data) return <Loading />;
  const e = earnings.data;
  return (
    <div className="stack-lg narrow-page">
      <h1>Earnings</h1>
      <div className="card stats">
        <div className="stat"><span className="small muted">Owed to you</span><strong>{formatMoney(e.owed)}</strong></div>
        <div className="stat"><span className="small muted">Earned</span><strong>{formatMoney(e.earned)}</strong></div>
        <div className="stat"><span className="small muted">Given back in refunds</span><strong>{formatMoney(e.refunded)}</strong></div>
      </div>
      <p className="small muted">Your share is the rent less a 10% service fee. A refund takes back your share of it in the same proportion; damage taken from a deposit is all yours.</p>
      {e.lines.length === 0 ? (
        <Empty title="Nothing yet" icon={<Wallet size={24} />}><span className="muted">Money appears here when a rental of yours is confirmed.</span></Empty>
      ) : (
        <div className="table-card card">
          <table className="table">
            <thead><tr><th>When</th><th>What</th><th className="right">Amount</th></tr></thead>
            <tbody>
              {e.lines.map((l) => (
                <tr key={`${l.entryId}-${l.amount}`}>
                  <td className="nowrap small">{formatDayTime(l.postedAt)}</td>
                  <td>{l.bookingId ? <a {...linkProps(`/rentals/${l.bookingId}`)}>{kindLabel[l.kind] ?? l.kind}</a> : kindLabel[l.kind] ?? l.kind}</td>
                  <td className={`right mono ${l.amount < 0 ? "danger-text" : ""}`}>{l.amount > 0 ? "+" : ""}{formatMoney(l.amount)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
}

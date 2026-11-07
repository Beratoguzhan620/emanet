import { CalendarCheck, ImageOff } from "lucide-react";
import { api } from "../api";
import { StatusBadge } from "../components/Badges";
import { Empty, Failed, Loading } from "../components/States";
import { useLoad, useNow } from "../hooks";
import { formatMoney, formatRange, isOpen, relative } from "../format";
import { linkProps } from "../router";
import type { BookingSummary } from "../types";

function Row({ item, now }: { item: BookingSummary; now: number }) {
  const waiting = item.status === "Requested";
  return (
    <a className="rental-row card" {...linkProps(`/rentals/${item.id}`)}>
      <div className="rental-thumb">{item.listing.thumbUrl ? <img src={item.listing.thumbUrl} alt="" loading="lazy" /> : <ImageOff size={18} aria-hidden="true" />}</div>
      <div className="min0 grow">
        <strong className="ellipsis block">{item.listing.title}</strong>
        <span className="small muted ellipsis block">{formatRange(item.start, item.end)} · {item.isOwner ? "rented by" : "from"} {item.other.name}</span>
      </div>
      <div className="rental-meta">
        <StatusBadge status={item.status} />
        <span className="small muted">{waiting ? `answer ${relative(item.respondBy, now)}` : formatMoney(item.rent)}</span>
      </div>
    </a>
  );
}

export function Rentals({ as }: { as: "renter" | "owner" }) {
  const [list, reload] = useLoad(() => api.myBookings(as), [as]);
  const now = useNow(30_000);
  const items = list.data?.items ?? [];
  const open = items.filter((i) => isOpen(i.status));
  const past = items.filter((i) => !isOpen(i.status));
  const asking = as === "owner" ? open.filter((i) => i.status === "Requested") : [];
  const rest = open.filter((i) => !asking.includes(i));

  return (
    <div className="stack-lg narrow-page">
      <div className="page-head">
        <h1>Rentals</h1>
        <div className="tabs" role="tablist">
          <a role="tab" aria-selected={as === "renter"} className={`tab${as === "renter" ? " active" : ""}`} {...linkProps("/rentals")}>Things I rent</a>
          <a role="tab" aria-selected={as === "owner"} className={`tab${as === "owner" ? " active" : ""}`} {...linkProps("/rentals?as=owner")}>Things I rent out</a>
        </div>
      </div>
      {list.state === "failed" && <Failed message={list.error} onRetry={reload} />}
      {list.state === "loading" && !list.data && <Loading />}
      {list.data && items.length === 0 && (
        <Empty title={as === "owner" ? "Nobody has rented from you yet" : "You have not rented anything yet"} icon={<CalendarCheck size={24} />}>
          <a className="button primary" {...linkProps(as === "owner" ? "/my/listings/new" : "/")}>{as === "owner" ? "List something" : "Find something to rent"}</a>
        </Empty>
      )}
      {asking.length > 0 && <section className="stack-8" aria-label="Waiting for your answer"><h2>Waiting for your answer</h2>{asking.map((i) => <Row key={i.id} item={i} now={now} />)}</section>}
      {rest.length > 0 && <section className="stack-8" aria-label="Under way"><h2>Under way</h2>{rest.map((i) => <Row key={i.id} item={i} now={now} />)}</section>}
      {past.length > 0 && <section className="stack-8" aria-label="Past"><h2>Past</h2>{past.map((i) => <Row key={i.id} item={i} now={now} />)}</section>}
    </div>
  );
}

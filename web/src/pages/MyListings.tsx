import { ImageOff, Package, Plus } from "lucide-react";
import { api } from "../api";
import { Rating } from "../components/Badges";
import { Empty, Failed, Loading } from "../components/States";
import { useLoad } from "../hooks";
import { categoryLabel, formatMoney } from "../format";
import { linkProps } from "../router";

export function MyListings() {
  const [list, reload] = useLoad(() => api.myListings(), []);
  const [earnings] = useLoad(() => api.earnings(), []);
  const items = list.data ?? [];

  return (
    <div className="stack-lg narrow-page">
      <div className="page-head">
        <h1>My listings</h1>
        <a className="button primary" {...linkProps("/my/listings/new")}><Plus size={15} />List something</a>
      </div>
      {earnings.data && (
        <a className="card stats" {...linkProps("/earnings")}>
          <div className="stat"><span className="small muted">Owed to you</span><strong>{formatMoney(earnings.data.owed)}</strong></div>
          <div className="stat"><span className="small muted">Earned so far</span><strong>{formatMoney(earnings.data.earned)}</strong></div>
          <div className="stat"><span className="small muted">Given back</span><strong>{formatMoney(earnings.data.refunded)}</strong></div>
        </a>
      )}
      {list.state === "failed" && <Failed message={list.error} onRetry={reload} />}
      {list.state === "loading" && !list.data && <Loading />}
      {list.data && items.length === 0 && (
        <Empty title="Nothing listed yet" icon={<Package size={24} />}>
          <span className="muted">A drill used twice a year, a tent, a projector: someone nearby needs it this weekend.</span>
          <a className="button primary" {...linkProps("/my/listings/new")}>List your first thing</a>
        </Empty>
      )}
      <div className="stack-8">
        {items.map((item) => (
          <a key={item.id} className="rental-row card" {...linkProps(`/my/listings/${item.id}`)}>
            <div className="rental-thumb">{item.thumbUrl ? <img src={item.thumbUrl} alt="" loading="lazy" /> : <ImageOff size={18} aria-hidden="true" />}</div>
            <div className="min0 grow">
              <strong className="ellipsis block">{item.title}</strong>
              <span className="small muted ellipsis block">{categoryLabel[item.category]} · {item.area} · {formatMoney(item.pricePerDay)} a day</span>
            </div>
            <div className="rental-meta">
              <span className={`badge ${item.status === "Active" ? "ok" : "plain"}`}>{item.status === "Active" ? "Listed" : "Paused"}</span>
              <Rating value={item.rating} count={item.ratingCount} />
            </div>
          </a>
        ))}
      </div>
    </div>
  );
}

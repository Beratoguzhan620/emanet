import { ImageOff, LocateFixed, Search, SearchX } from "lucide-react";
import { useEffect, useMemo, useState } from "react";
import { api, query, type SearchFilters } from "../api";
import { Rating } from "../components/Badges";
import { MapView, type Pin } from "../components/MapView";
import { Empty, Failed } from "../components/States";
import { useDebounced, useLoad } from "../hooks";
import { addDays, categories, categoryLabel, formatMoney, km, plural, today } from "../format";
import { linkProps, navigate } from "../router";
import type { Category, ListingSummary, Sort } from "../types";

const sorts: { key: Sort; label: string; needsPlace?: boolean }[] = [
  { key: "Relevance", label: "Best match" },
  { key: "Distance", label: "Nearest", needsPlace: true },
  { key: "PriceLow", label: "Cheapest" },
  { key: "PriceHigh", label: "Dearest" },
  { key: "Newest", label: "Newest" },
];

function readFilters(params: URLSearchParams): SearchFilters {
  const number = (key: string) => (params.get(key) ? Number(params.get(key)) : undefined);
  return {
    q: params.get("q") ?? undefined,
    category: (params.get("category") as Category | null) ?? undefined,
    from: params.get("from") ?? undefined,
    to: params.get("to") ?? undefined,
    maxPrice: number("maxPrice"),
    lat: number("lat"),
    lng: number("lng"),
    radiusKm: number("radiusKm"),
    sort: (params.get("sort") as Sort | null) ?? undefined,
  };
}

function ResultCard({ item, dates }: { item: ListingSummary; dates: string }) {
  return (
    <a className="result card" {...linkProps(`/l/${item.id}${dates}`)}>
      <div className="result-photo">
        {item.thumbUrl ? <img src={item.thumbUrl} alt="" loading="lazy" width={480} height={360} /> : <ImageOff size={22} aria-hidden="true" />}
      </div>
      <div className="result-body">
        <div className="row between gap-6">
          <span className="small muted">{categoryLabel[item.category]}</span>
          <Rating value={item.rating} count={item.ratingCount} />
        </div>
        <strong className="result-title">{item.title}</strong>
        <span className="small muted ellipsis">{item.area}{item.distanceKm !== null ? ` · ${km(item.distanceKm)}` : ""}</span>
        <div className="row between">
          <span><strong>{formatMoney(item.pricePerDay)}</strong><span className="small muted"> / day</span></span>
          {item.weeklyPercentOff > 0 && <span className="badge accent">{item.weeklyPercentOff}% off a week</span>}
        </div>
      </div>
    </a>
  );
}

export function SearchPage({ params }: { params: URLSearchParams }) {
  const filters = useMemo(() => readFilters(params), [params]);
  const [text, setText] = useState(filters.q ?? "");
  const [pages, setPages] = useState(1);
  const [locating, setLocating] = useState<string | null>(null);
  const q = useDebounced(text, 300);

  const update = (change: Partial<SearchFilters>) => {
    const next = { ...filters, ...change };
    if (next.sort === "Distance" && next.lat === undefined) next.sort = undefined;
    navigate(`/${query({ ...next })}`, true);
  };

  useEffect(() => { if ((filters.q ?? "") !== q) update({ q: q || undefined }); }, [q]); // eslint-disable-line react-hooks/exhaustive-deps
  useEffect(() => setPages(1), [params]);

  const key = params.toString();
  const [results, reload] = useLoad(async () => {
    const all: ListingSummary[] = [];
    let total = 0;
    for (let page = 1; page <= pages; page++) {
      const result = await api.search({ ...filters, page, pageSize: 24 });
      all.push(...result.items);
      total = result.total;
    }
    return { items: all, total };
  }, [key, pages]);

  const dates = filters.from && filters.to ? query({ start: filters.from, end: filters.to }) : "";
  const items = results.data?.items ?? [];
  const pins = useMemo<Pin[]>(() => items.map((item) => ({
    id: item.id, latitude: item.latitude, longitude: item.longitude, label: `${item.title}, ${formatMoney(item.pricePerDay)} a day`,
    text: `${Math.round(item.pricePerDay)}₺`, tone: "accent", onClick: () => navigate(`/l/${item.id}${dates}`),
  })), [items, dates]);

  function nearMe() {
    if (!("geolocation" in navigator)) { setLocating("This browser cannot tell where you are."); return; }
    setLocating("Finding you…");
    navigator.geolocation.getCurrentPosition(
      (p) => { setLocating(null); update({ lat: Math.round(p.coords.latitude * 1e4) / 1e4, lng: Math.round(p.coords.longitude * 1e4) / 1e4, radiusKm: 10, sort: "Distance" }); },
      () => setLocating("Your location is not shared, so results are not sorted by distance."),
      { timeout: 10_000, maximumAge: 300_000 },
    );
  }

  const datesWrong = !!filters.from && !!filters.to && filters.to <= filters.from;

  return (
    <div className="search-page">
      <div className="search-bar card">
        <label className="search grow">
          <Search size={16} aria-hidden="true" />
          <span className="sr-only">What do you need?</span>
          <input className="input" placeholder="Drill, tent, projector…" value={text} onChange={(e) => setText(e.target.value)} type="search" />
        </label>
        <label className="field inline">From<input className="input date-input" type="date" min={today()} value={filters.from ?? ""} onChange={(e) => update({ from: e.target.value || undefined, to: filters.to && e.target.value && filters.to <= e.target.value ? addDays(e.target.value, 1) : filters.to })} /></label>
        <label className="field inline">Until<input className="input date-input" type="date" min={filters.from ? addDays(filters.from, 1) : today(1)} value={filters.to ?? ""} onChange={(e) => update({ to: e.target.value || undefined })} /></label>
        <label className="field inline">Up to<input className="input small-input" type="number" min={1} step={50} placeholder="₺ a day" value={filters.maxPrice ?? ""} onChange={(e) => update({ maxPrice: e.target.value ? Number(e.target.value) : undefined })} /></label>
        <label className="field inline">Sort
          <select className="select" value={filters.sort ?? "Relevance"} onChange={(e) => update({ sort: e.target.value as Sort })}>
            {sorts.filter((s) => !s.needsPlace || filters.lat !== undefined).map((s) => <option key={s.key} value={s.key}>{s.label}</option>)}
          </select>
        </label>
        <button className={`button${filters.lat !== undefined ? " primary" : ""}`} onClick={filters.lat !== undefined ? () => update({ lat: undefined, lng: undefined, radiusKm: undefined }) : nearMe}>
          <LocateFixed size={15} />{filters.lat !== undefined ? "Near me · on" : "Near me"}
        </button>
      </div>

      <div className="chips" role="group" aria-label="Category">
        <button className={`chip${!filters.category ? " on" : ""}`} onClick={() => update({ category: undefined })} aria-pressed={!filters.category}>Everything</button>
        {categories.map((c) => (
          <button key={c} className={`chip${filters.category === c ? " on" : ""}`} onClick={() => update({ category: filters.category === c ? undefined : c })} aria-pressed={filters.category === c}>
            {categoryLabel[c]}
          </button>
        ))}
      </div>
      {locating && <p className="small muted" role="status">{locating}</p>}
      {datesWrong && <p className="form-error" role="alert">The return day comes after the pickup day.</p>}

      <div className="search-grid">
        <section aria-label="Results" className="stack">
          <div className="row between">
            <span className="small muted" role="status">
              {results.state === "loading" && !results.data ? "Searching…" : results.data ? `${plural(results.data.total, "thing")} to rent${filters.from && filters.to ? " on those days" : ""}` : ""}
            </span>
          </div>
          {results.state === "failed" && <Failed message={results.error} onRetry={reload} />}
          {results.state === "loading" && !results.data && (
            <div className="results" aria-hidden="true">{Array.from({ length: 6 }, (_, i) => <div key={i} className="result card"><div className="result-photo skeleton" /><div className="result-body"><div className="skeleton" /><div className="skeleton" style={{ width: "60%" }} /></div></div>)}</div>
          )}
          {results.data && items.length === 0 && (
            <Empty title="Nothing matches" icon={<SearchX size={24} />}>
              <span className="muted">Try other words, another category or other days.</span>
            </Empty>
          )}
          {items.length > 0 && <div className="results">{items.map((item) => <ResultCard key={item.id} item={item} dates={dates} />)}</div>}
          {results.data && items.length < results.data.total && (
            <button className="button center-self" onClick={() => setPages(pages + 1)} disabled={results.state === "loading"}>{results.state === "loading" ? "Loading…" : "Show more"}</button>
          )}
        </section>
        <aside className="search-map" aria-label="Map of results">
          <MapView pins={pins} height="100%" fitKey={key} circles={filters.lat !== undefined && filters.lng !== undefined ? [{ latitude: filters.lat, longitude: filters.lng, radiusMeters: (filters.radiusKm ?? 25) * 1000, tone: "muted" }] : []} />
          <p className="map-note small muted">Pins show the area, not the address. The exact place is shared once a rental is confirmed.</p>
        </aside>
      </div>
    </div>
  );
}

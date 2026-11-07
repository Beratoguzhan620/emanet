// A weekend rush, measured against the running stack: hundreds of people asking for the same few listings on
// overlapping days at once, owners accepting at once, and searches throughout. Afterwards it checks what must hold
// however the requests interleaved: no listing is booked twice for a day, everyone who lost has their holds let go,
// and the books balance and match the payment provider.
// Usage: AUTH_ATTEMPTS_PER_MINUTE=100000 docker compose up --build -d
//        node tests/load/load.mjs [renters=300] [listings=20]
const BASE = process.env.BASE_URL ?? "http://localhost:8080";
const RENTERS = Number(process.argv[2] ?? 300);
const LISTINGS = Number(process.argv[3] ?? 20);
const ADMIN = process.env.ADMIN_EMAIL ?? "support@emanet.local";
const run = Date.now().toString(36);
const password = "load-test-password";

const latencies = { request: [], accept: [], search: [], read: [] };
const statuses = new Map();
let errors5xx = 0;

async function call(path, token, body, method = body ? "POST" : "GET", bucket) {
  const started = performance.now();
  const response = await fetch(BASE + path, {
    method,
    headers: { ...(body ? { "Content-Type": "application/json" } : {}), ...(token ? { Authorization: `Bearer ${token}` } : {}) },
    body: body ? JSON.stringify(body) : undefined,
  });
  const text = await response.text();
  const ms = performance.now() - started;
  if (bucket) latencies[bucket].push(ms);
  statuses.set(response.status, (statuses.get(response.status) ?? 0) + 1);
  if (response.status >= 500) errors5xx++;
  let json = null;
  try { json = text ? JSON.parse(text) : null; } catch { json = null; }
  return { status: response.status, json, ms };
}

async function must(path, token, body, method, bucket) {
  const r = await call(path, token, body, method, bucket);
  if (r.status >= 400) throw new Error(`${method ?? (body ? "POST" : "GET")} ${path}: ${r.status} ${JSON.stringify(r.json)}`);
  return r.json;
}

async function pool(items, limit, task) {
  const results = new Array(items.length);
  let next = 0;
  await Promise.all(Array.from({ length: Math.min(limit, items.length) }, async () => {
    while (next < items.length) {
      const i = next++;
      results[i] = await task(items[i], i);
    }
  }));
  return results;
}

function stats(values) {
  const sorted = [...values].sort((a, b) => a - b);
  const at = (p) => sorted[Math.min(sorted.length - 1, Math.floor((p / 100) * sorted.length))] ?? 0;
  return { count: sorted.length, p50: Math.round(at(50)), p95: Math.round(at(95)), p99: Math.round(at(99)), max: Math.round(sorted.at(-1) ?? 0) };
}

const report = (name, data) => console.log(`${name.padEnd(36)} ${typeof data === "string" ? data : JSON.stringify(data)}`);
const sleep = (ms) => new Promise((resolve) => setTimeout(resolve, ms));
const pick = (list) => list[Math.floor(Math.random() * list.length)];
const failures = [];
const check = (ok, what) => { report(ok ? "ok" : "FAILED", what); if (!ok) failures.push(what); };

function day(plus) {
  const istanbul = new Intl.DateTimeFormat("en-CA", { timeZone: "Europe/Istanbul", year: "numeric", month: "2-digit", day: "2-digit" }).format(new Date());
  const d = new Date(`${istanbul}T00:00:00Z`);
  d.setUTCDate(d.getUTCDate() + plus);
  return d.toISOString().slice(0, 10);
}

async function signIn(email, name, secret = password) {
  const r = await call("/api/auth/register", null, { email, displayName: name, password: secret });
  if (r.status === 201) return r.json.accessToken;
  return (await must("/api/auth/login", null, { email, password: secret })).accessToken;
}

const setupStarted = performance.now();
const ownerToken = await signIn(`owner.${run}@example.com`, "Yük Testi");
// Support may already have an account on this stack, made by another script: ADMIN_PASSWORD is its password.
const adminToken = await signIn(ADMIN, "Destek", process.env.ADMIN_PASSWORD ?? password);
// Half the listings book instantly, half wait for the owner; all in Atakum and Ilkadım.
const listings = await pool(Array.from({ length: LISTINGS }, (_, i) => i), 8, (i) => must("/api/listings", ownerToken, {
  title: `${pick(["Matkap", "Çadır", "Projeksiyon", "Bisiklet", "Kamp sandalyesi"])} ${i + 1} ${run}`, description: "Yük testi ilanı.",
  category: pick(["Tools", "Camping", "Electronics", "Sports"]), pricePerDay: 100 + (i % 5) * 25, weeklyPercentOff: 10, deposit: 500,
  latitude: 41.30 + (i % 10) * 0.004, longitude: 36.26 + Math.floor(i / 10) * 0.01, area: "Atakum, Samsun", minDays: 1, maxDays: 10, instantBook: i % 2 === 0,
}));
const renters = await pool(Array.from({ length: RENTERS }, (_, i) => i), 24, async (i) => ({ token: await signIn(`kiraci${i}.${run}@example.com`, `Kiracı ${i}`) }));
report("setup", `${LISTINGS} listings and ${RENTERS} renters in ${Math.round((performance.now() - setupStarted) / 1000)} s`);

// Everyone wants something for the coming two weeks, in 1 to 4 day stretches: heavy overlap on few listings.
const wishes = renters.map((renter) => {
  const start = 1 + Math.floor(Math.random() * 12);
  return { renter, listing: pick(listings), start: day(start), end: day(start + 1 + Math.floor(Math.random() * 4)) };
});

let searching = true;
const searches = (async () => {
  while (searching) {
    await call(`/api/listings?q=${encodeURIComponent(pick(["matkap", "çadır", "proje", "bisiklet", "sandalye"]))}&lat=41.31&lng=36.27&radiusKm=10&from=${day(3)}&to=${day(5)}`, null, null, "GET", "search");
  }
})();

const rushStarted = performance.now();
const results = await pool(wishes, 64, async (w) => {
  const r = await call("/api/bookings", w.renter.token, { listingId: w.listing.id, start: w.start, end: w.end, message: "", paymentMethod: "pm_card_ok" }, "POST", "request");
  return { ...w, status: r.status, booking: r.json };
});
report("rush", `${wishes.length} requests in ${Math.round(performance.now() - rushStarted)} ms`);

// The owner answers everything waiting at once, in random order; each acceptance declines the rivals it beats.
const waiting = results.filter((r) => r.status === 201 && r.booking.status === "Requested").sort(() => Math.random() - 0.5);
const acceptStarted = performance.now();
const answers = await pool(waiting, 32, (w) => call(`/api/bookings/${w.booking.id}/accept`, ownerToken, {}, "POST", "accept"));
report("accept", `${waiting.length} acceptances in ${Math.round(performance.now() - acceptStarted)} ms`);
searching = false;
await searches;

const count = (list, status) => list.filter((x) => x.status === status).length;
report("requests", { created: count(results, 201), taken: count(results, 409), other: results.length - count(results, 201) - count(results, 409) });
report("acceptances", { ok: count(answers, 200), refused: count(answers, 409), other: answers.length - count(answers, 200) - count(answers, 409) });

// Whatever the interleaving, the calendar has no day twice, and every confirmed rental is on it.
let overlaps = 0;
let booked = 0;
for (const listing of listings) {
  const ranges = (await must(`/api/listings/${listing.id}/calendar?from=${day(0)}`, ownerToken, null, "GET", "read")).sort((a, b) => a.start.localeCompare(b.start));
  booked += ranges.length;
  for (let i = 1; i < ranges.length; i++) if (ranges[i].start < ranges[i - 1].end) overlaps++;
}
const mine = await must("/api/me/bookings?role=owner&pageSize=100&status=Confirmed", ownerToken);
check(overlaps === 0, `no listing is booked twice for any day (${booked} rentals on the calendars)`);
check(mine.total === booked, `every confirmed rental is on a calendar (${mine.total} confirmed)`);

// Holds of every request that lost are let go: at once, or by the sweeper's next pass.
const lost = results.filter((r) => r.status === 201).map((r) => r.booking.id);
let stillHeld = [];
for (let attempt = 0; attempt < 8; attempt++) {
  const views = await pool(lost, 16, (id) => call(`/api/bookings/${id}`, ownerToken, null, "GET", "read"));
  stillHeld = views.filter((v) => v.json && ["Declined", "Expired", "Cancelled"].includes(v.json.status) && !v.json.payment.holdsReleased);
  const uncharged = views.filter((v) => v.json && v.json.status === "Confirmed" && !v.json.payment.rentCharged);
  if (stillHeld.length === 0 && uncharged.length === 0) break;
  await sleep(10_000);
}
check(stillHeld.length === 0, "every declined request had its holds let go");

const books = await must("/api/admin/books", adminToken);
check(books.balanced && books.ledgerTotal === 0, `every journal entry balances (${books.entries} entries)`);
check(books.reconciled, `the ledger matches the payment provider (${books.providerClearing} TL held)`);
check(errors5xx === 0, `no server errors (${errors5xx})`);

report("latency: ask to rent (ms)", stats(latencies.request));
report("latency: accept (ms)", stats(latencies.accept));
report("latency: search during the rush (ms)", stats(latencies.search));
report("latency: reads (ms)", stats(latencies.read));
report("responses", Object.fromEntries([...statuses].sort((a, b) => a[0] - b[0])));
process.exit(failures.length === 0 ? 0 : 1);

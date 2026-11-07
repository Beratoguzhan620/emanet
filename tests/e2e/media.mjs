// Screenshots and a walkthrough for the README, against the running Compose stack, with a believable neighbourhood.
// Usage: AUTH_ATTEMPTS_PER_MINUTE=100000 docker compose up --build -d
//        cd tests/e2e && node media.mjs [output folder]
import { chromium } from "playwright";
import { mkdirSync } from "node:fs";

const BASE = process.env.BASE_URL ?? "http://localhost:8080";
const OUT = process.argv[2] ?? new URL("./output/media/", import.meta.url).pathname;
const ADMIN = process.env.ADMIN_EMAIL ?? "support@emanet.local";
mkdirSync(`${OUT}frames`, { recursive: true });
const run = Date.now().toString(36);
const password = "media-password-123";

async function call(path, token, body, method = body ? "POST" : "GET", raw) {
  const response = await fetch(BASE + path, {
    method,
    headers: { ...(raw ? { "Content-Type": "image/jpeg" } : body ? { "Content-Type": "application/json" } : {}), ...(token ? { Authorization: `Bearer ${token}` } : {}) },
    body: raw ?? (body ? JSON.stringify(body) : undefined),
  });
  const text = await response.text();
  if (!response.ok) throw new Error(`${method} ${path}: ${response.status} ${text}`);
  return text ? JSON.parse(text) : null;
}

async function person(name, email = `${name.split(" ")[0].toLowerCase()}.${run}@example.com`) {
  const r = await fetch(`${BASE}/api/auth/register`, { method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify({ email, displayName: name, password }) });
  const session = r.ok ? await r.json() : await call("/api/auth/login", null, { email, password });
  return { name, email, token: session.accessToken, session };
}

function day(plus) {
  const istanbul = new Intl.DateTimeFormat("en-CA", { timeZone: "Europe/Istanbul", year: "numeric", month: "2-digit", day: "2-digit" }).format(new Date());
  const d = new Date(`${istanbul}T00:00:00Z`);
  d.setUTCDate(d.getUTCDate() + plus);
  return d.toISOString().slice(0, 10);
}

const browser = await chromium.launch();
const painter = await (await browser.newContext()).newPage();

/** Flat drawings of the things for rent, made in a canvas: no photographs are copied from anywhere. */
async function art(kind) {
  const base64 = await painter.evaluate((kind) => {
    const c = document.createElement("canvas");
    c.width = 1200;
    c.height = 900;
    const g = c.getContext("2d");
    const ink = "#1F2A37", petrol = "#1F5C73", sand = "#D9C9A8", soft = "#E2EEF1", warm = "#B5651D", grey = "#8A867F";
    const bg = { drill: "#EFE9DD", tent: "#E4ECE6", camera: "#ECE8F0", bike: "#E9EEF2", projector: "#F1ECE4", ladder: "#EEF0EA", chairs: "#F2EADF", speaker: "#E7EBEE" }[kind] ?? "#EFEDE8";
    g.fillStyle = bg;
    g.fillRect(0, 0, 1200, 900);
    g.fillStyle = "rgba(31,42,55,0.08)";
    g.beginPath(); g.ellipse(600, 760, 380, 40, 0, 0, Math.PI * 2); g.fill();
    const rect = (x, y, w, h, color, r = 18) => { g.fillStyle = color; g.beginPath(); g.roundRect(x, y, w, h, r); g.fill(); };
    const circle = (x, y, r, color) => { g.fillStyle = color; g.beginPath(); g.arc(x, y, r, 0, Math.PI * 2); g.fill(); };
    const line = (points, color, width) => { g.strokeStyle = color; g.lineWidth = width; g.lineCap = "round"; g.lineJoin = "round"; g.beginPath(); points.forEach(([x, y], i) => (i ? g.lineTo(x, y) : g.moveTo(x, y))); g.stroke(); };
    if (kind === "drill") {
      rect(330, 300, 470, 170, petrol, 40); rect(800, 350, 170, 70, grey, 12); rect(960, 372, 120, 26, ink, 6);
      rect(430, 450, 140, 260, ink, 30); rect(400, 690, 200, 80, petrol, 20); circle(450, 385, 30, soft);
    } else if (kind === "tent") {
      g.fillStyle = petrol; g.beginPath(); g.moveTo(240, 740); g.lineTo(600, 220); g.lineTo(960, 740); g.closePath(); g.fill();
      g.fillStyle = ink; g.beginPath(); g.moveTo(520, 740); g.lineTo(600, 420); g.lineTo(680, 740); g.closePath(); g.fill();
      line([[600, 220], [600, 170]], ink, 10); line([[240, 740], [180, 770]], grey, 6); line([[960, 740], [1020, 770]], grey, 6);
    } else if (kind === "camera") {
      rect(300, 330, 600, 380, ink, 40); rect(400, 270, 180, 90, ink, 20); circle(600, 520, 150, grey); circle(600, 520, 110, petrol); circle(600, 520, 60, ink); circle(560, 480, 18, soft); rect(780, 370, 70, 40, sand, 8);
    } else if (kind === "bike") {
      line([[380, 600], [520, 420], [760, 420], [820, 600]], petrol, 22); line([[520, 420], [600, 600], [380, 600]], petrol, 22); line([[600, 600], [760, 420]], petrol, 22);
      g.lineWidth = 22; g.strokeStyle = ink; g.beginPath(); g.arc(380, 600, 130, 0, Math.PI * 2); g.stroke(); g.beginPath(); g.arc(820, 600, 130, 0, Math.PI * 2); g.stroke();
      line([[500, 380], [560, 380]], ink, 18); line([[760, 420], [740, 340], [800, 330]], ink, 14);
    } else if (kind === "projector") {
      rect(300, 380, 600, 260, soft, 40); rect(300, 380, 600, 260, "rgba(31,42,55,0.06)", 40); circle(760, 510, 90, ink); circle(760, 510, 55, petrol); rect(380, 450, 200, 24, grey, 8); rect(380, 500, 140, 24, grey, 8); rect(360, 630, 60, 50, ink, 10); rect(780, 630, 60, 50, ink, 10);
    } else if (kind === "ladder") {
      line([[460, 160], [400, 760]], warm, 26); line([[740, 160], [800, 760]], warm, 26);
      for (let i = 0; i < 6; i++) { const y = 240 + i * 95; line([[452 - i * 6, y], [748 + i * 6, y]], sand, 20); }
    } else if (kind === "chairs") {
      for (const x of [380, 760]) { rect(x - 120, 420, 240, 70, petrol, 16); rect(x - 110, 250, 220, 190, soft, 16); line([[x - 110, 490], [x - 150, 740]], ink, 16); line([[x + 110, 490], [x + 150, 740]], ink, 16); line([[x - 110, 490], [x + 150, 740]], grey, 10); }
    } else {
      rect(420, 220, 360, 540, ink, 40); circle(600, 590, 110, grey); circle(600, 590, 60, petrol); circle(600, 360, 60, grey); circle(600, 360, 28, petrol);
    }
    return c.toDataURL("image/jpeg", 0.88).split(",")[1];
  }, kind);
  return Buffer.from(base64, "base64");
}

// The people: three owners around Samsun, renters, and support.
const mehmet = await person("Mehmet Usta");
const elif = await person("Elif Şahin");
const burak = await person("Burak Yıldız");
const ayse = await person("Ayşe Kaya");
const can = await person("Can Demir");
const support = await person("Destek", ADMIN);

const listings = {};
for (const [key, owner, kind, title, category, price, weekly, deposit, lat, lng, area, instant, description] of [
  ["drill", mehmet, "drill", "Darbeli matkap, Bosch 750 W", "Tools", 150, 20, 1000, 41.3341, 36.2712, "Atakum, Samsun", false, "Beton ve ahşap için. Çantası, 12 parça uç seti ve yedek kömürleri ile."],
  ["ladder", mehmet, "ladder", "Alüminyum merdiven, 7 basamak", "Tools", 90, 15, 500, 41.3318, 36.2766, "Atakum, Samsun", true, "Boya, perde, ampul işleri için. Katlanınca bagaja sığar."],
  ["tent", elif, "tent", "Kamp çadırı, 4 kişilik", "Camping", 220, 25, 1500, 41.2894, 36.3297, "İlkadım, Samsun", true, "Çift katlı, su geçirmez. Kazıklar ve taşıma çantası dahil."],
  ["chairs", elif, "chairs", "Katlanır kamp sandalyesi, 2 adet", "Camping", 60, 10, 300, 41.2911, 36.3342, "İlkadım, Samsun", true, "Hafif, bardaklıklı. Plaj ve piknik için."],
  ["camera", burak, "camera", "Aynasız fotoğraf makinesi ve 35 mm lens", "Photo", 450, 30, 6000, 41.2768, 36.3439, "Canik, Samsun", false, "Düğün ve gezi çekimleri için. İki batarya, 64 GB kart, şarj aleti."],
  ["projector", burak, "projector", "Projeksiyon cihazı, Full HD", "Electronics", 280, 20, 3000, 41.2803, 36.3398, "Canik, Samsun", false, "Maç akşamları ve sunumlar için. HDMI kablosu ve perde dahil."],
  ["bike", elif, "bike", "Şehir bisikleti, 28 jant", "Sports", 120, 25, 1200, 41.3165, 36.2987, "Atakum, Samsun", true, "Sahil yolu için ideal. Kask ve kilit dahil."],
  ["speaker", mehmet, "speaker", "Taşınabilir hoparlör, 40 W", "Music", 110, 10, 800, 41.3355, 36.2689, "Atakum, Samsun", true, "Bahçe partileri için. 12 saat pil."],
]) {
  const listing = await call("/api/listings", owner.token, {
    title, description, category, pricePerDay: price, weeklyPercentOff: weekly, deposit, latitude: lat, longitude: lng, area, minDays: 1, maxDays: 14, instantBook: instant,
  });
  await call(`/api/listings/${listing.id}/photos`, owner.token, null, "POST", await art(kind));
  if (key === "drill" || key === "camera") await call(`/api/listings/${listing.id}/photos`, owner.token, null, "POST", await art(kind === "drill" ? "speaker" : "projector"));
  listings[key] = { ...listing, owner };
}

const book = (who, key, start, end, message = "") => call("/api/bookings", who.token, { listingId: listings[key].id, start: day(start), end: day(end), message, paymentMethod: "pm_card_ok" });
const codeOf = async (who, id, field) => (await call(`/api/bookings/${id}`, who.token)).handover[field];

// A finished rental with reviews on both sides, so the listings have stars.
for (const [key, renter, stars, comment] of [
  ["tent", can, 5, "Çadır tertemiz ve eksiksizdi. Elif Hanım kurulumu da anlattı."],
  ["ladder", ayse, 5, "Tam zamanında, çok sağlam."],
  ["chairs", can, 4, "Rahat sandalyeler, biri biraz gıcırdıyor."],
]) {
  const owner = listings[key].owner;
  const b = await book(renter, key, 0, 2);
  if (b.status === "Requested") await call(`/api/bookings/${b.id}/accept`, owner.token, {});
  await call(`/api/bookings/${b.id}/pickup`, owner.token, { code: await codeOf(renter, b.id, "pickupCode") });
  await call(`/api/bookings/${b.id}/return`, renter.token, { code: await codeOf(owner, b.id, "returnCode") });
  await call(`/api/bookings/${b.id}/all-good`, owner.token, {});
  await call(`/api/bookings/${b.id}/review`, renter.token, { stars, comment });
  await call(`/api/bookings/${b.id}/review`, owner.token, { stars: 5, comment: "Özenle kullanmış, zamanında getirdi." });
}

// What is going on today: the camera is out, a drill request is waiting, the projector is booked for the weekend.
const out = await book(ayse, "camera", 0, 3, "Kuzenimin düğünü için.");
await call(`/api/bookings/${out.id}/accept`, burak.token, {});
await call(`/api/bookings/${out.id}/photos?stage=Pickup`, ayse.token, null, "POST", await art("camera"));
await call(`/api/bookings/${out.id}/pickup`, burak.token, { code: await codeOf(ayse, out.id, "pickupCode") });
const later = await book(can, "projector", 4, 6, "Cumartesi maç akşamı için.");
await call(`/api/bookings/${later.id}/accept`, burak.token, {});
await book(can, "drill", 9, 11, "Mutfak dolaplarını asacağım.");
await call(`/api/listings/${listings.bike.id}/blocks`, elif.token, { start: day(6), end: day(9), note: "Bakımda" });
// A damage claim waiting for support.
const claimed = await book(can, "speaker", 0, 1, "Doğum günü partisi.");
await call(`/api/bookings/${claimed.id}/pickup`, mehmet.token, { code: await codeOf(can, claimed.id, "pickupCode") });
await call(`/api/bookings/${claimed.id}/return`, can.token, { code: await codeOf(mehmet, claimed.id, "returnCode") });
await call(`/api/bookings/${claimed.id}/photos?stage=Damage`, mehmet.token, null, "POST", await art("speaker"));
await call(`/api/bookings/${claimed.id}/claim`, mehmet.token, { amount: 350, note: "Kumaş ön panel yırtılmış; değişimi 350 TL." });
await call(`/api/bookings/${claimed.id}/claim/reject`, can.token, { note: "Teslim alırken de yırtıktı." });

async function signedIn(who, viewport, options = {}) {
  const context = await browser.newContext({ viewport, deviceScaleFactor: 2, locale: "en-GB", timezoneId: "Europe/Istanbul", ...options });
  const page = await context.newPage();
  if (who) {
    await page.goto(`${BASE}/signin`);
    await page.evaluate((s) => localStorage.setItem("emanet.session", JSON.stringify(s)), who.session);
  }
  return page;
}
const settle = async (page, ms = 1500) => { await page.waitForLoadState("networkidle").catch(() => {}); await page.waitForTimeout(ms); };
const phoneOptions = { isMobile: true, hasTouch: true };

const visitor = await signedIn(null, { width: 1440, height: 900 });
await visitor.goto(`${BASE}/`);
await visitor.locator(".result").nth(5).waitFor();
await settle(visitor, 2500);
await visitor.screenshot({ path: `${OUT}search.png` });

const owner = await signedIn(burak, { width: 1440, height: 900 });
await owner.goto(`${BASE}/rentals/${out.id}`);
await owner.locator(".code-big").waitFor();
await settle(owner, 2000);
await owner.screenshot({ path: `${OUT}rental-owner.png` });

const staff = await signedIn(support, { width: 1440, height: 900 });
await staff.goto(`${BASE}/admin/${claimed.id}`);
await staff.getByRole("heading", { name: "Decide" }).waitFor();
await settle(staff, 2000);
await staff.screenshot({ path: `${OUT}claim.png`, fullPage: true });
await staff.goto(`${BASE}/admin`);
await staff.getByText("all balanced", { exact: false }).waitFor();
await settle(staff);
await staff.screenshot({ path: `${OUT}books.png` });

const elifDesk = await signedIn(elif, { width: 1440, height: 900 });
await elifDesk.goto(`${BASE}/my/listings/${listings.bike.id}`);
await elifDesk.getByRole("heading", { name: "Calendar" }).waitFor();
await settle(elifDesk);
await elifDesk.getByRole("heading", { name: "Calendar" }).scrollIntoViewIfNeeded();
await elifDesk.screenshot({ path: `${OUT}owner-calendar.png` });

// The walkthrough: Ayşe finds a drill on her phone, picks days, asks; Mehmet accepts; she gets a code; he types it in.
const desk = await signedIn(mehmet, { width: 1280, height: 800 });
const phone = await signedIn(ayse, { width: 390, height: 844 }, phoneOptions);
let frame = 0;
const shoot = async (label) => {
  const n = String(frame++).padStart(2, "0");
  await desk.screenshot({ path: `${OUT}frames/${n}-desk.png` });
  await phone.screenshot({ path: `${OUT}frames/${n}-phone.png` });
  console.log(`frame ${n}: ${label}`);
};

await desk.goto(`${BASE}/my/listings`);
await desk.locator(".rental-row").first().waitFor();
await phone.goto(`${BASE}/`);
await phone.locator(".result").first().waitFor();
await settle(phone, 2000);
await settle(desk, 500);
await shoot("search on the phone");
await phone.getByRole("searchbox").fill("matkap");
await phone.locator(".result").filter({ hasText: "Darbeli matkap" }).waitFor();
await settle(phone, 1200);
await shoot("found the drill");
await phone.locator(".result").filter({ hasText: "Darbeli matkap" }).click();
await phone.getByRole("heading", { name: "Darbeli matkap, Bosch 750 W" }).waitFor();
await settle(phone, 1500);
await shoot("the listing");
await phone.getByRole("heading", { name: "When it is free" }).scrollIntoViewIfNeeded();
await phone.locator(".day:not(:disabled)").filter({ hasText: /^\d+$/ }).first().waitFor();
await phone.getByLabel(day(1), { exact: true }).click();
await phone.getByLabel(day(3), { exact: true }).click();
await settle(phone, 800);
await shoot("days picked on the calendar");
await phone.locator(".booking-panel").scrollIntoViewIfNeeded();
await phone.locator(".sums .total").waitFor();
await settle(phone, 800);
await phone.screenshot({ path: `${OUT}listing-phone.png` });
await shoot("the price for those days");
await phone.getByLabel(/A note for/).fill("Banyo dolabını asacağım, iki gün yeter.");
await phone.getByRole("button", { name: "Ask to rent" }).click();
await phone.waitForURL(/\/rentals\//);
await settle(phone, 1200);
const bookingId = phone.url().split("/").pop();
await desk.goto(`${BASE}/rentals?as=owner`);
await desk.getByRole("heading", { name: "Waiting for your answer" }).waitFor();
await settle(desk, 800);
await shoot("the request reaches the owner");
await desk.goto(`${BASE}/rentals/${bookingId}`);
await desk.getByRole("button", { name: "Accept and charge the rent" }).waitFor();
await settle(desk, 800);
await shoot("the owner reads it");
await desk.getByRole("button", { name: "Accept and charge the rent" }).click();
await desk.getByLabel("Money").getByText("Rent charged").waitFor();
await phone.reload();
await phone.locator(".code-big").waitFor();
await settle(phone, 1200);
await phone.screenshot({ path: `${OUT}code-phone.png` });
await shoot("accepted: the renter gets a code and the exact place");

// For the walkthrough the rental starts today, so the owner can take the code; the API refuses earlier pickups.
const today = await book(ayse, "drill", 0, 1, "Hemen lazım oldu.").catch(() => null);
if (today) {
  await call(`/api/bookings/${today.id}/accept`, mehmet.token, {});
  await desk.goto(`${BASE}/rentals/${today.id}`);
  const field = desk.getByLabel(/The code on .*screen/);
  await field.waitFor();
  await phone.goto(`${BASE}/rentals/${today.id}`);
  await phone.locator(".code-big").waitFor();
  await settle(phone, 800);
  await field.fill(await codeOf(ayse, today.id, "pickupCode"));
  await settle(desk, 400);
  await shoot("the owner types in the renter's code at the handover");
  await desk.getByRole("button", { name: "Handed over" }).click();
  await desk.locator(".code-big").waitFor();
  await phone.reload();
  await phone.getByText("With the renter").first().waitFor();
  await settle(phone, 1000);
  await settle(desk, 400);
  await shoot("handed over: now the owner holds the return code");
}

await browser.close();
console.log(`media written to ${OUT} (${frame} frames)`);

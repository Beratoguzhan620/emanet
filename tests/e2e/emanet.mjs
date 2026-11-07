// An owner, a renter, a rival and support, in separate browsers, against the whole Compose stack: a drill is listed,
// found, asked for with a refused card and then a good one, accepted, handed over and back with codes, claimed for
// damage, disputed and decided, and reviewed. Two people then try to book the same days at the same moment.
// Usage: AUTH_ATTEMPTS_PER_MINUTE=1000 docker compose up --build -d
//        cd tests/e2e && npm install && npx playwright install chromium && node emanet.mjs
import { chromium } from "playwright";
import { mkdirSync } from "node:fs";

const BASE = process.env.BASE_URL ?? "http://localhost:8080";
const OUT = new URL("./output/", import.meta.url).pathname;
mkdirSync(OUT, { recursive: true });

const run = Date.now().toString(36);
const password = "e2e-test-password";
const owner = { name: "Mehmet Usta", email: `mehmet.${run}@example.com` };
const renter = { name: "Ayşe Kaya", email: `ayse.${run}@example.com` };
const rival = { name: "Can Demir", email: `can.${run}@example.com` };
const support = { name: "Destek", email: process.env.ADMIN_EMAIL ?? "support@emanet.local" };

const browser = await chromium.launch();
const errors = [];
const pages = {};
let failures = 0;

async function step(name, action) {
  try {
    await action();
    console.log(`ok   ${name}`);
  } catch (error) {
    failures++;
    console.log(`FAIL ${name}\n     ${String(error.message ?? error).split("\n").slice(0, 3).join("\n     ")}`);
    for (const [label, page] of Object.entries(pages)) await page.screenshot({ path: `${OUT}${label}-${failures}.png`, fullPage: true }).catch(() => {});
  }
}

async function open(label, viewport = { width: 1360, height: 900 }, options = {}) {
  const context = await browser.newContext({ viewport, locale: "en-GB", timezoneId: "Europe/Istanbul", ...options });
  const page = await context.newPage();
  page.on("pageerror", (error) => errors.push(`${label}: ${error.message}`));
  page.on("console", (message) => {
    // Refused requests the test makes on purpose show up as failed resources; they are checked on screen instead.
    if (message.type() === "error" && !/Failed to load resource|tile\.openstreetmap/.test(message.text())) errors.push(`${label}: ${message.text()}`);
  });
  pages[label] = page;
  return page;
}

/** Calls the API as whoever is signed in on the page. Returns the status and body; never throws for an HTTP error. */
async function call(page, method, path, body) {
  return page.evaluate(async ({ method, path, body }) => {
    const session = JSON.parse(localStorage.getItem("emanet.session") ?? "null");
    const headers = { "Content-Type": "application/json" };
    if (session) headers.Authorization = `Bearer ${session.accessToken}`;
    const response = await fetch(path, { method, headers, body: body ? JSON.stringify(body) : undefined });
    const text = await response.text();
    return { status: response.status, body: text ? JSON.parse(text) : null };
  }, { method, path, body });
}

async function register(who) {
  const response = await fetch(`${BASE}/api/auth/register`, { method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify({ displayName: who.name, email: who.email, password }) });
  // Support may be there from an earlier run against the same database.
  if (!response.ok && !(response.status === 409 && who === support)) throw new Error(`register ${who.email}: ${response.status}`);
}

async function signIn(page, who, next) {
  await page.goto(`${BASE}/signin?next=${encodeURIComponent(next)}`);
  await page.getByLabel("Email").fill(who.email);
  await page.getByLabel("Password").fill(password);
  await page.getByRole("button", { name: "Sign in" }).click();
  await page.waitForURL((url) => url.pathname === next.split("?")[0]);
}

/** A JPEG drawn in the browser, so the test needs no image files. */
async function photo(page, color) {
  const base64 = await page.evaluate((color) => {
    const canvas = document.createElement("canvas");
    canvas.width = 800;
    canvas.height = 600;
    const g = canvas.getContext("2d");
    g.fillStyle = "#F7F6F3";
    g.fillRect(0, 0, 800, 600);
    g.fillStyle = color;
    g.fillRect(160, 220, 420, 120);
    g.fillRect(560, 250, 140, 60);
    g.fillRect(220, 340, 90, 180);
    return canvas.toDataURL("image/jpeg", 0.9).split(",")[1];
  }, color);
  return { name: "photo.jpg", mimeType: "image/jpeg", buffer: Buffer.from(base64, "base64") };
}

const day = (plus) => {
  const istanbul = new Intl.DateTimeFormat("en-CA", { timeZone: "Europe/Istanbul", year: "numeric", month: "2-digit", day: "2-digit" }).format(new Date());
  const d = new Date(`${istanbul}T00:00:00Z`);
  d.setUTCDate(d.getUTCDate() + plus);
  return d.toISOString().slice(0, 10);
};

for (const who of [owner, renter, rival, support]) await register(who);
const desk = await open("owner");
const phone = await open("renter", { width: 390, height: 844 }, { isMobile: true, hasTouch: true });
const visitor = await open("visitor");
const staff = await open("support");
const title = `Darbeli matkap ${run}`;
let listingId = "";
let bookingId = "";

await step("an owner lists a drill with a place on the map and a photo", async () => {
  await signIn(desk, owner, "/my/listings");
  await desk.getByText("Nothing listed yet").waitFor();
  await desk.getByRole("link", { name: "List something" }).first().click();
  await desk.getByLabel("Title").fill(title);
  await desk.getByLabel("Neighbourhood").fill("Atakum, Samsun");
  await desk.getByLabel("Description").fill("Beton ve ahşap için. Çantası ve uç seti ile.");
  await desk.getByLabel("Price a day (₺)").fill("150");
  await desk.getByLabel("Off for a week (%)").fill("20");
  await desk.getByLabel("Deposit (₺)").fill("1000");
  const map = await desk.locator(".leaflet-container").boundingBox();
  await desk.mouse.click(map.x + map.width / 2 + 40, map.y + map.height / 2 - 30);
  await desk.getByRole("button", { name: "Create the listing" }).click();
  await desk.waitForURL(/\/my\/listings\/[0-9a-f-]{36}$/);
  listingId = desk.url().split("/").pop();
  await desk.locator("input[type=file]").setInputFiles(await photo(desk, "#1F5C73"));
  await desk.getByText("1 of 8").waitFor({ timeout: 15_000 });
  const listing = await call(desk, "GET", `/api/listings/${listingId}`);
  if (!listing.body.place.exact || listing.body.photos.length !== 1) throw new Error("the owner should see the exact place and one photo");
});

await step("a visitor finds it, sees the area but not the address, and must sign in to rent", async () => {
  await visitor.goto(`${BASE}/?q=${run}`);
  await visitor.locator(".result").filter({ hasText: title }).click();
  await visitor.getByRole("heading", { name: title }).waitFor();
  await visitor.getByText("Within about 400 m").waitFor();
  const listing = await call(visitor, "GET", `/api/listings/${listingId}`);
  if (listing.body.place.exact) throw new Error("a visitor got the exact place");
  await visitor.getByRole("button", { name: "Sign in to rent" }).waitFor();
});

await step("a refused card holds nothing; a good one asks for three days", async () => {
  await signIn(phone, renter, `/l/${listingId}?start=${day(0)}&end=${day(3)}`);
  await phone.locator(".sums").getByText("₺450").first().waitFor();
  await phone.getByLabel("Pay with").selectOption("pm_card_declined");
  await phone.getByRole("button", { name: "Ask to rent" }).click();
  await phone.locator(".form-error").waitFor();
  const none = await call(phone, "GET", "/api/me/bookings?role=renter");
  if (none.body.items.length !== 0) throw new Error("a refused card left a request behind");
  await phone.getByLabel("Pay with").selectOption("pm_card_ok");
  await phone.getByLabel(/A note for/).fill("Hafta sonu banyo dolabı için.");
  await phone.getByRole("button", { name: "Ask to rent" }).click();
  await phone.waitForURL(/\/rentals\/[0-9a-f-]{36}$/);
  bookingId = phone.url().split("/").pop();
  await phone.getByText("Waiting for the owner").first().waitFor();
  await phone.getByText("Rent held, not charged").waitFor();
});

await step("the owner accepts; the rent is charged and the renter gets a code and the exact place", async () => {
  await desk.goto(`${BASE}/rentals?as=owner`);
  await desk.getByRole("heading", { name: "Waiting for your answer" }).waitFor();
  await desk.locator(".rental-row").filter({ hasText: title }).click();
  await desk.getByText("Hafta sonu banyo dolabı için.").waitFor();
  await desk.getByRole("button", { name: "Accept and charge the rent" }).click();
  await desk.getByLabel("Money").getByText("Rent charged").waitFor();
  await phone.reload();
  await phone.locator(".code-big").waitFor();
  await phone.getByText("Exact place").waitFor();
});

await step("the handover takes the code from the renter's screen, and a wrong one is refused", async () => {
  const code = (await phone.locator(".code-big").textContent()).replace(/\s/g, "");
  await desk.reload();
  const field = desk.getByLabel(/The code on .*screen/);
  await field.fill(code === "000000" ? "111111" : "000000");
  await desk.getByRole("button", { name: "Handed over" }).click();
  await desk.getByText("That code is not right").waitFor();
  await field.fill(code);
  await desk.getByRole("button", { name: "Handed over" }).click();
  await desk.getByText("With the renter").first().waitFor();
});

await step("both add condition photos that strangers cannot open", async () => {
  await phone.reload();
  const file = await photo(phone, "#8A867F");
  const [chooser] = await Promise.all([phone.waitForEvent("filechooser"), phone.getByRole("button", { name: "At pickup" }).click()]);
  await chooser.setFiles(file);
  await phone.locator(".photo-tile").first().waitFor({ timeout: 15_000 });
  const booking = await call(phone, "GET", `/api/bookings/${bookingId}`);
  const stranger = await open("stranger");
  await stranger.goto(BASE);
  const response = await call(stranger, "GET", booking.body.photos[0].url);
  if (response.status !== 401) throw new Error(`a stranger got ${response.status} for a condition photo`);
});

await step("the item comes back with the code from the owner's screen", async () => {
  await desk.reload();
  const code = (await desk.locator(".code-big").textContent()).replace(/\s/g, "");
  await phone.reload();
  await phone.getByLabel(/The code on .*screen/).fill(code);
  await phone.getByRole("button", { name: "Given back" }).click();
  await phone.getByText("Returned", { exact: true }).first().waitFor();
});

await step("the owner claims damage, the renter disagrees, and support decides on half", async () => {
  await desk.reload();
  await desk.getByRole("button", { name: "At return" }).waitFor();
  await desk.getByRole("button", { name: "Report damage" }).click();
  await desk.getByLabel("What it costs to fix (₺)").fill("200");
  await desk.getByLabel("What happened").fill("Mandren gevşek, uç tutmuyor; tamir 200 TL.");
  await desk.getByRole("button", { name: "Send the claim" }).click();
  await desk.getByText("Damage claim").first().waitFor();
  await phone.reload();
  await phone.getByRole("button", { name: "Disagree, ask support" }).click();
  await phone.getByRole("dialog").getByLabel("Your side, briefly").fill("Teslim alırken de gevşekti, fotoğrafta görünüyor.");
  await phone.getByRole("dialog").getByRole("button", { name: "Send to support" }).click();
  await phone.getByText("Support will look at the photos and decide").waitFor();
  await signIn(staff, support, "/admin");
  await staff.locator(".rental-row").filter({ hasText: title }).getByText("Needs a decision").click();
  await staff.getByLabel("Take from the deposit (₺)").fill("100");
  await staff.getByLabel("Note to both sides").fill("Yarı yarıya.");
  await staff.getByRole("button", { name: "Settle the claim" }).click();
  await staff.getByText("Completed", { exact: true }).first().waitFor();
  await phone.reload();
  await phone.getByText(/₺100 of the deposit went to Mehmet Usta/).waitFor();
});

await step("both sides review once, and the listing shows it", async () => {
  await phone.getByRole("heading", { name: "How was it?" }).waitFor();
  await phone.getByRole("radio", { name: "5 of 5" }).click();
  await phone.getByLabel("A few words, if you like").fill("Matkap iyiydi, Mehmet Bey çok yardımcı oldu.");
  await phone.getByRole("button", { name: "Post the review" }).click();
  await phone.getByRole("heading", { name: "Reviews" }).waitFor();
  await desk.reload();
  await desk.getByRole("radio", { name: "4 of 5" }).click();
  await desk.getByRole("button", { name: "Post the review" }).click();
  await desk.getByRole("heading", { name: "Reviews" }).waitFor();
  const again = await call(phone, "POST", `/api/bookings/${bookingId}/review`, { stars: 1 });
  if (again.status !== 409) throw new Error(`a second review got ${again.status}`);
  await visitor.goto(`${BASE}/l/${listingId}`);
  await visitor.getByText("Matkap iyiydi").waitFor();
  await visitor.getByLabel("Rated 5 out of 5 by 1").first().waitFor();
});

await step("the owner's earnings and the books add up", async () => {
  await desk.goto(`${BASE}/earnings`);
  // 450 rent less the 10% fee, plus 100 from the deposit.
  await desk.locator(".stat").filter({ hasText: "Owed to you" }).getByText("₺505").waitFor();
  await staff.goto(`${BASE}/admin`);
  await staff.getByText("all balanced", { exact: false }).waitFor();
  const books = await call(staff, "GET", "/api/admin/books");
  if (!books.body.balanced || !books.body.reconciled) throw new Error(`the books do not add up: ${JSON.stringify(books.body)}`);
  const refused = await call(desk, "GET", "/api/admin/books");
  if (refused.status !== 403) throw new Error(`an owner got ${refused.status} for the books`);
});

await step("two people booking the same days at the same moment: one gets them, the other is told", async () => {
  const instant = await call(desk, "POST", "/api/listings", {
    title: `Kamp çadırı ${run}`, description: "Dört kişilik.", category: "Camping", pricePerDay: 120, weeklyPercentOff: 0, deposit: 500,
    latitude: 41.33, longitude: 36.27, area: "Atakum, Samsun", minDays: 1, maxDays: 14, instantBook: true,
  });
  const other = await open("rival");
  await signIn(other, rival, "/rentals");
  const body = { listingId: instant.body.id, start: day(5), end: day(8), message: "", paymentMethod: "pm_card_ok" };
  const [a, b] = await Promise.all([call(phone, "POST", "/api/bookings", body), call(other, "POST", "/api/bookings", body)]);
  const statuses = [a.status, b.status].sort();
  if (statuses.join() !== "201,409") throw new Error(`expected one 201 and one 409, got ${statuses.join()}`);
  const loser = a.status === 409 ? phone : other;
  await loser.goto(`${BASE}/l/${instant.body.id}?start=${day(5)}&end=${day(8)}`);
  await loser.getByText("Some of these days are taken.").waitFor();
  const stranger = await call(other, "GET", `/api/bookings/${bookingId}`);
  if (stranger.status !== 404) throw new Error(`someone else's rental gave ${stranger.status}`);
});

if (errors.length > 0) {
  failures++;
  console.log(`FAIL the browsers reported errors\n     ${[...new Set(errors)].join("\n     ")}`);
}

await browser.close();
console.log(failures === 0 ? "\nAll steps passed." : `\n${failures} step(s) failed.`);
process.exit(failures === 0 ? 0 : 1);

import { describe, expect, it } from "vitest";
import { addDays, daysBetween, formatRange, initials, rentFor, today } from "./format";

describe("format", () => {
  it("prices a week with the discount like the server", () => {
    expect(rentFor(150, 20, 6)).toBe(900);
    expect(rentFor(150, 20, 7)).toBe(840);
    expect(rentFor(3.33, 15, 7)).toBe(19.81);
  });

  it("counts days on the calendar, not in hours", () => {
    expect(addDays("2026-10-31", 1)).toBe("2026-11-01");
    expect(daysBetween("2026-11-07", "2026-11-14")).toBe(7);
  });

  it("knows today in Turkey even when it is still yesterday in London", () => {
    // 22:30 UTC on 6 November is 01:30 on 7 November in Istanbul.
    expect(today(0, Date.parse("2026-11-06T22:30:00Z"))).toBe("2026-11-07");
    expect(today(2, Date.parse("2026-11-06T20:30:00Z"))).toBe("2026-11-08");
  });

  it("writes ranges briefly", () => {
    expect(formatRange("2026-11-11", "2026-11-14")).toBe("11–14 Nov");
    expect(formatRange("2026-11-28", "2026-12-02")).toBe("28 Nov – 2 Dec");
  });

  it("takes initials the Turkish way", () => {
    expect(initials("ilker ışık")).toBe("İI");
    expect(initials("Ayşe")).toBe("A");
  });
});

import { ChevronLeft, ChevronRight } from "lucide-react";
import { useMemo, useState } from "react";
import { addDays, today } from "../format";
import type { CalendarRange } from "../types";

const monthName = new Intl.DateTimeFormat("en-GB", { month: "long", year: "numeric", timeZone: "UTC" });
const weekdays = ["Mo", "Tu", "We", "Th", "Fr", "Sa", "Su"];

/** Whether any day in [start, end) is taken. Ranges are half-open too: a return day is free for the next pickup. */
export function overlaps(ranges: CalendarRange[], start: string, end: string) {
  return ranges.some((r) => r.start < end && start < r.end);
}

function rangeOf(ranges: CalendarRange[], day: string) {
  return ranges.find((r) => r.start <= day && day < r.end);
}

/**
 * Two months of days. Taken days cannot be picked; the first click is the pickup day, the second the return day.
 * The owner's view marks which days are rentals and which they closed.
 */
export function Calendar({ ranges, start, end, onPick, owner = false }: {
  ranges: CalendarRange[]; start?: string; end?: string; onPick?: (start: string, end: string | undefined) => void; owner?: boolean;
}) {
  const first = today();
  const [offset, setOffset] = useState(0);
  const months = useMemo(() => {
    const base = new Date(`${first.slice(0, 7)}-01T00:00:00Z`);
    return [0, 1].map((i) => {
      const m = new Date(Date.UTC(base.getUTCFullYear(), base.getUTCMonth() + offset + i, 1));
      const startDay = m.toISOString().slice(0, 10);
      const lead = (m.getUTCDay() + 6) % 7;
      const length = new Date(Date.UTC(m.getUTCFullYear(), m.getUTCMonth() + 1, 0)).getUTCDate();
      return { label: monthName.format(m), lead, days: Array.from({ length }, (_, d) => addDays(startDay, d)) };
    });
  }, [offset, first]);

  function click(day: string) {
    if (!onPick) return;
    if (!start || end || day <= start) {
      if (!rangeOf(ranges, day)) onPick(day, undefined);
      return;
    }
    if (!overlaps(ranges, start, day)) onPick(start, day);
    else if (!rangeOf(ranges, day)) onPick(day, undefined);
  }

  return (
    <div className="calendar">
      <div className="row between">
        <button type="button" className="icon-button" onClick={() => setOffset(offset - 1)} disabled={offset === 0} aria-label="Earlier months"><ChevronLeft size={18} /></button>
        <span className="small muted">{start && !end ? "Now pick the return day" : onPick ? "Pick the pickup day" : ""}</span>
        <button type="button" className="icon-button" onClick={() => setOffset(offset + 1)} disabled={offset >= 10} aria-label="Later months"><ChevronRight size={18} /></button>
      </div>
      <div className="months">
        {months.map((month) => (
          <div key={month.label} className="month">
            <strong className="small">{month.label}</strong>
            <div className="days" role="grid" aria-label={month.label}>
              {weekdays.map((w) => <span key={w} className="weekday" aria-hidden="true">{w}</span>)}
              {Array.from({ length: month.lead }, (_, i) => <span key={`lead-${i}`} />)}
              {month.days.map((day) => {
                const range = rangeOf(ranges, day);
                const past = day < first;
                const selected = !!start && (day === start || (!!end && day > start && day < end));
                const isReturn = !!end && day === end;
                const kind = range ? (owner ? range.kind.toLowerCase() : "taken") : "";
                const label = `${day}${range ? (owner && range.kind === "Closed" ? ", closed" : ", taken") : ""}${selected ? ", selected" : ""}${isReturn ? ", return day" : ""}`;
                return (
                  <button key={day} type="button" className={`day ${kind}${past ? " past" : ""}${selected ? " selected" : ""}${isReturn ? " return" : ""}`}
                    disabled={past || !onPick} onClick={() => click(day)} aria-label={label} title={owner && range?.note ? range.note : undefined}>
                    {Number(day.slice(8))}
                  </button>
                );
              })}
            </div>
          </div>
        ))}
      </div>
      <div className="legend small muted">
        <span><i className="swatch selected" />Your days</span>
        {owner ? <><span><i className="swatch booked" />Rented</span><span><i className="swatch closed" />Closed</span></> : <span><i className="swatch taken" />Taken</span>}
      </div>
    </div>
  );
}

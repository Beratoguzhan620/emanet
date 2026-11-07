import { Camera, ImageOff, Loader2 } from "lucide-react";
import { useEffect, useRef, useState } from "react";
import { api } from "../api";
import { formatDayTime } from "../format";
import type { Booking, HandoverPhoto, Stage } from "../types";
import { Dialog } from "./Dialog";

/** A private photo: fetched with the token, shown from memory, let go when it leaves the screen. */
function PrivateImage({ url, alt, className }: { url: string; alt: string; className?: string }) {
  const [src, setSrc] = useState<string | null>(null);
  const [failed, setFailed] = useState(false);
  useEffect(() => {
    let live = true;
    let made: string | null = null;
    api.photoBlob(url).then((u) => { made = u; if (live) setSrc(u); else URL.revokeObjectURL(u); }, () => live && setFailed(true));
    return () => { live = false; if (made) URL.revokeObjectURL(made); };
  }, [url]);
  if (failed) return <span className={`${className ?? ""} photo-missing`}><ImageOff size={18} aria-label="Could not load" /></span>;
  if (!src) return <span className={`${className ?? ""} skeleton`} aria-hidden="true" />;
  return <img className={className} src={src} alt={alt} />;
}

const stageLabel: Record<Stage, string> = { Pickup: "At pickup", Return: "At return", Damage: "Damage" };

/** Which stages this person can add photos to now; the server checks the same rules. */
export function openStages(booking: Booking): Stage[] {
  const stages: Stage[] = [];
  if (booking.status === "Confirmed" || booking.status === "Active") stages.push("Pickup");
  if (booking.status === "Active" || booking.status === "Returned") stages.push("Return");
  if (booking.isOwner && (booking.status === "Returned" || booking.status === "Disputed")) stages.push("Damage");
  return stages;
}

export function ConditionPhotos({ booking, onAdded, readOnly = false }: { booking: Booking; onAdded: (photo: HandoverPhoto) => void; readOnly?: boolean }) {
  const input = useRef<HTMLInputElement>(null);
  const [stage, setStage] = useState<Stage | null>(null);
  const [busy, setBusy] = useState<Stage | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [open, setOpen] = useState<HandoverPhoto | null>(null);
  const stages = readOnly ? [] : openStages(booking);
  const groups = (["Pickup", "Return", "Damage"] as Stage[]).map((s) => ({ stage: s, photos: booking.photos.filter((p) => p.stage === s) })).filter((g) => g.photos.length > 0);

  async function upload(files: FileList | null) {
    if (!files || !stage) return;
    setBusy(stage);
    setError(null);
    try {
      for (const file of Array.from(files).slice(0, 8)) onAdded(await api.addConditionPhoto(booking.id, stage, file));
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setBusy(null);
      if (input.current) input.current.value = "";
    }
  }

  return (
    <section className="card stack" aria-label="Condition photos">
      <div className="row between wrap">
        <h2>Condition photos</h2>
        <div className="row gap-6 wrap">
          {stages.map((s) => (
            <button key={s} className="button small" onClick={() => { setStage(s); input.current?.click(); }} disabled={busy !== null}>
              {busy === s ? <Loader2 size={14} className="spin" /> : <Camera size={14} />}{stageLabel[s]}
            </button>
          ))}
        </div>
      </div>
      <input ref={input} type="file" accept="image/jpeg,image/png,image/webp" multiple hidden onChange={(e) => void upload(e.target.files)} />
      {error && <p className="form-error" role="alert">{error}</p>}
      {groups.length === 0 ? (
        <p className="small muted">{stages.length > 0 ? "Take a few photos at the handover. Only the two of you, and support if there is a claim, can see them." : "No photos were added."}</p>
      ) : groups.map((g) => (
        <div key={g.stage} className="stack-8">
          <span className="small muted">{stageLabel[g.stage]}</span>
          <div className="photo-grid">
            {g.photos.map((p) => (
              <button key={p.id} className="photo-tile" onClick={() => setOpen(p)} aria-label={`${stageLabel[p.stage]} photo by ${p.by}`}>
                <PrivateImage url={p.thumbUrl} alt="" />
              </button>
            ))}
          </div>
        </div>
      ))}
      {open && (
        <Dialog title={`${stageLabel[open.stage]} · ${open.by}`} onClose={() => setOpen(null)} wide>
          <PrivateImage url={open.url} alt={`${stageLabel[open.stage]} photo by ${open.by}`} className="photo-large" />
          <p className="small muted">{formatDayTime(open.at)}</p>
        </Dialog>
      )}
    </section>
  );
}

const GAP = 6;
const WIDTH = 200;
let active = null;

function place(button, menu, placement) {
  menu.style.boxSizing = "border-box";
  menu.style.width = `${WIDTH}px`;
  menu.style.maxWidth = "none";

  const b = button.getBoundingClientRect();
  const mw = WIDTH;
  const mh = menu.offsetHeight;
  const vw = window.innerWidth;
  const vh = window.innerHeight;

  let vert = "side";
  if (placement.startsWith("Top")) vert = "top";
  else if (placement.startsWith("Bottom")) vert = "bottom";
  let top, left;

  if (vert === "side") {
    let toRight = placement === "Right";
    if (toRight && b.right + GAP + mw > vw && b.left - GAP - mw >= GAP) toRight = false;
    if (!toRight && b.left - GAP - mw < GAP && b.right + GAP + mw <= vw) toRight = true;
    left = toRight ? b.right + GAP : b.left - GAP - mw;
    top = b.top;
  } else {
    let below = vert === "bottom";
    if (below && b.bottom + GAP + mh > vh && b.top - GAP - mh >= GAP) below = false;
    if (!below && b.top - GAP - mh < GAP && b.bottom + GAP + mh <= vh) below = true;
    top = below ? b.bottom + GAP : b.top - GAP - mh;
    left = placement.endsWith("Right") ? b.right - mw : b.left;
  }

  left = Math.min(Math.max(GAP, left), Math.max(GAP, vw - mw - GAP));
  top = Math.min(Math.max(GAP, top), Math.max(GAP, vh - mh - GAP));

  menu.style.position = "fixed";
  menu.style.top = `${Math.round(top)}px`;
  menu.style.left = `${Math.round(left)}px`;
  menu.style.visibility = "visible";
}

export function open(button, menu, placement) {
  close();
  const reposition = () => place(button, menu, placement);
  reposition();
  window.addEventListener("scroll", reposition, true);
  window.addEventListener("resize", reposition);
  active = { reposition };
}

export function close() {
  if (!active) return;
  window.removeEventListener("scroll", active.reposition, true);
  window.removeEventListener("resize", active.reposition);
  active = null;
}

const states = new WeakMap();

function stripOf(viewport) {
  return viewport.querySelector(".timeline-strip");
}

function apply(viewport, state, animate) {
  const strip = stripOf(viewport);
  if (!strip) return;
  const max = viewport.clientHeight * 0.95;
  state.offset = Math.max(-max, Math.min(max, state.offset));
  strip.style.transition = animate ? "transform 0.18s ease-out" : "none";
  strip.style.transform = `translateY(${-state.offset}px)`;
}

function settle(viewport, state) {
  const h = viewport.clientHeight;
  if (Math.abs(state.offset) > h * 0.22) {
    const direction = state.offset > 0 ? 1 : -1;
    state.committing = true;
    state.offset = direction * h;
    apply(viewport, state, true);
    setTimeout(() => {
      state.dotnet.invokeMethodAsync("CommitScrollPan", direction);
    }, 180);
  } else {
    state.offset = 0;
    apply(viewport, state, true);
  }
}

export function init(viewport, dotnet) {
  const state = { dotnet, offset: 0, committing: false, idleTimer: null, touchY: null };
  states.set(viewport, state);

  state.onWheel = e => {
    e.preventDefault();
    if (state.committing) return;
    state.offset += e.deltaY;
    apply(viewport, state, false);
    if (state.idleTimer) clearTimeout(state.idleTimer);
    state.idleTimer = setTimeout(() => settle(viewport, state), 140);
  };
  state.onTouchStart = e => {
    if (e.touches.length === 1) state.touchY = e.touches[0].clientY;
  };
  state.onTouchMove = e => {
    if (state.touchY == null || state.committing) return;
    e.preventDefault();
    state.offset += state.touchY - e.touches[0].clientY;
    state.touchY = e.touches[0].clientY;
    apply(viewport, state, false);
  };
  state.onTouchEnd = () => {
    if (state.touchY == null) return;
    state.touchY = null;
    if (!state.committing) settle(viewport, state);
  };

  viewport.addEventListener("wheel", state.onWheel, { passive: false });
  viewport.addEventListener("touchstart", state.onTouchStart, { passive: true });
  viewport.addEventListener("touchmove", state.onTouchMove, { passive: false });
  viewport.addEventListener("touchend", state.onTouchEnd);
}

export function reset(viewport) {
  const state = states.get(viewport);
  if (!state) return;
  state.offset = 0;
  state.committing = false;
  const strip = stripOf(viewport);
  if (strip) {
    strip.style.transition = "none";
    strip.style.transform = "";
  }
}

export function destroy(viewport) {
  const state = states.get(viewport);
  if (!state) return;
  viewport.removeEventListener("wheel", state.onWheel);
  viewport.removeEventListener("touchstart", state.onTouchStart);
  viewport.removeEventListener("touchmove", state.onTouchMove);
  viewport.removeEventListener("touchend", state.onTouchEnd);
  states.delete(viewport);
}

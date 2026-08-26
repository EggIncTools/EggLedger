const states = new WeakMap();

const COMMIT_THRESHOLD = 0.22;
const DRAG_LIMIT = 0.95;
const SNAP_MS = 180;
const REST_CLEAR_MS = 220;
const IDLE_MS = 140;

function stripOf(viewport) {
  return viewport.querySelector(".timeline-strip");
}

function stepOf(viewport) {
  const slot = viewport.querySelector(".timeline-period");
  const height = slot ? slot.getBoundingClientRect().height : 0;
  return height > 1 ? height : viewport.clientHeight;
}

function normalizeWheel(viewport, e) {
  if (e.deltaMode === 1) return e.deltaY * 16;
  if (e.deltaMode === 2) return e.deltaY * viewport.clientHeight;
  return e.deltaY;
}

function paint(viewport, state, animate) {
  const strip = stripOf(viewport);
  if (!strip) return;
  strip.style.transition = animate ? `transform ${SNAP_MS / 1000}s ease-out` : "none";
  strip.style.transform = `translateY(${-state.offset}px)`;
}

function clearTransform(viewport) {
  const strip = stripOf(viewport);
  if (!strip) return;
  strip.style.transition = "none";
  strip.style.transform = "";
}

function clearTimers(state) {
  if (state.idleTimer) clearTimeout(state.idleTimer);
  if (state.commitTimer) clearTimeout(state.commitTimer);
  if (state.restTimer) clearTimeout(state.restTimer);
  state.idleTimer = null;
  state.commitTimer = null;
  state.restTimer = null;
}

function drag(viewport, state, delta) {
  const limit = stepOf(viewport) * DRAG_LIMIT;
  state.offset = Math.max(-limit, Math.min(limit, state.offset + delta));
  paint(viewport, state, false);
}

function snapBack(viewport, state) {
  state.offset = 0;
  paint(viewport, state, true);
  if (state.restTimer) clearTimeout(state.restTimer);
  state.restTimer = setTimeout(() => {
    state.restTimer = null;
    if (state.offset === 0 && !state.committing) clearTransform(viewport);
  }, REST_CLEAR_MS);
}

function settle(viewport, state) {
  state.idleTimer = null;
  const step = stepOf(viewport);
  if (Math.abs(state.offset) <= step * COMMIT_THRESHOLD) {
    snapBack(viewport, state);
    return;
  }

  const direction = state.offset > 0 ? 1 : -1;
  state.committing = true;
  state.offset = direction * step;
  paint(viewport, state, true);
  state.commitTimer = setTimeout(() => {
    state.commitTimer = null;
    state.dotnet.invokeMethodAsync("CommitScrollPan", direction);
  }, SNAP_MS);
}

export function init(viewport, dotnet) {
  const state = {
    dotnet,
    offset: 0,
    committing: false,
    idleTimer: null,
    commitTimer: null,
    restTimer: null,
    touchY: null
  };
  states.set(viewport, state);

  state.onWheel = e => {
    e.preventDefault();
    if (state.committing) return;
    if (state.restTimer) {
      clearTimeout(state.restTimer);
      state.restTimer = null;
    }

    drag(viewport, state, normalizeWheel(viewport, e));
    if (state.idleTimer) clearTimeout(state.idleTimer);
    state.idleTimer = setTimeout(() => settle(viewport, state), IDLE_MS);
  };
  state.onTouchStart = e => {
    if (state.committing) return;
    if (e.touches.length === 1) state.touchY = e.touches[0].clientY;
  };
  state.onTouchMove = e => {
    if (state.touchY == null || state.committing) return;
    e.preventDefault();
    if (state.restTimer) {
      clearTimeout(state.restTimer);
      state.restTimer = null;
    }

    const y = e.touches[0].clientY;
    drag(viewport, state, state.touchY - y);
    state.touchY = y;
  };
  state.onTouchEnd = () => {
    if (state.touchY == null) return;
    state.touchY = null;
    if (state.committing) return;
    if (state.idleTimer) {
      clearTimeout(state.idleTimer);
      state.idleTimer = null;
    }

    settle(viewport, state);
  };

  viewport.addEventListener("wheel", state.onWheel, { passive: false });
  viewport.addEventListener("touchstart", state.onTouchStart, { passive: true });
  viewport.addEventListener("touchmove", state.onTouchMove, { passive: false });
  viewport.addEventListener("touchend", state.onTouchEnd);
  viewport.addEventListener("touchcancel", state.onTouchEnd);
}

export function reset(viewport) {
  const state = states.get(viewport);
  if (!state) return;
  clearTimers(state);
  state.offset = 0;
  state.committing = false;
  state.touchY = null;
  clearTransform(viewport);
}

export function destroy(viewport) {
  const state = states.get(viewport);
  if (!state) return;
  clearTimers(state);
  viewport.removeEventListener("wheel", state.onWheel);
  viewport.removeEventListener("touchstart", state.onTouchStart);
  viewport.removeEventListener("touchmove", state.onTouchMove);
  viewport.removeEventListener("touchend", state.onTouchEnd);
  viewport.removeEventListener("touchcancel", state.onTouchEnd);
  states.delete(viewport);
}

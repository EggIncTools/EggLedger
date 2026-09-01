(function () {
  if (window.__dropTooltipInit) return;
  window.__dropTooltipInit = true;

  function anchorOf(host) {
    return host.querySelector(".tt-anchor") || host.querySelector("a") || host;
  }

  function position(host, ev) {
    const mode = host.dataset.ttMode || "static";
    const rect = anchorOf(host).getBoundingClientRect();
    let x = rect.left + rect.width / 2;
    let y = rect.top;
    if (mode === "cursor") {
      x = ev.clientX;
      y = ev.clientY;
    } else if (mode === "cursor-x") {
      x = ev.clientX;
      y = host.getBoundingClientRect().top;
    } else if (mode === "cursor-y") {
      x = host.getBoundingClientRect().left;
      y = ev.clientY;
    }
    host.style.setProperty("--tt-left", x + "px");
    host.style.setProperty("--tt-top", y + "px");
    requestAnimationFrame(function () { edgeAdjust(host, y); });
  }

  function edgeAdjust(host, anchorY) {
    const tip = host.querySelector(".tooltip-floating");
    if (!tip) return;
    const r = tip.getBoundingClientRect();
    if (r.width === 0) return;
    tip.classList.toggle("tooltip-below", anchorY - r.height - 14 < 4);
    const rr = tip.getBoundingClientRect();
    let delta = 0;
    if (rr.left < 4) delta = 4 - rr.left;
    else if (rr.right > window.innerWidth - 4) delta = window.innerWidth - 4 - rr.right;
    const shift = (Number.parseFloat(host.style.getPropertyValue("--tt-shift-x")) || 0) + delta;
    host.style.setProperty("--tt-shift-x", shift + "px");
    tip.style.setProperty("--arrow-offset", -shift + "px");
  }

  document.addEventListener("mouseover", function (e) {
    const host = e.target?.closest?.(".tooltip-host");
    if (!host) return;
    host.style.setProperty("--tt-shift-x", "0px");
    position(host, e);
  });

  document.addEventListener("mousemove", function (e) {
    const host = e.target?.closest?.(".tooltip-host");
    if (!host) return;
    const mode = host.dataset.ttMode;
    if (mode === "cursor" || mode === "cursor-x" || mode === "cursor-y") position(host, e);
  });
})();

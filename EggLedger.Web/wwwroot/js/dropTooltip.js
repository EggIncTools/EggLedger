(function () {
  if (window.__dropTooltipInit) return;
  window.__dropTooltipInit = true;

  document.addEventListener("mouseover", function (e) {
    const host = e.target && e.target.closest ? e.target.closest(".tooltip-host") : null;
    if (!host) return;
    const anchor = host.querySelector("a") || host;
    const rect = anchor.getBoundingClientRect();
    host.style.setProperty("--tt-left", (rect.left + rect.width / 2) + "px");
    host.style.setProperty("--tt-top", rect.top + "px");
  });
})();

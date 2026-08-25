(function () {
  if (window.__dropTooltipInit) return;
  window.__dropTooltipInit = true;

  document.addEventListener("mouseover", function (e) {
    const host = e.target?.closest?.(".tooltip-host");
    if (!host) return;
    const anchor = host.querySelector(".tt-anchor") || host.querySelector("a") || host;
    const rect = anchor.getBoundingClientRect();
    host.style.setProperty("--tt-left", (rect.left + rect.width / 2) + "px");
    host.style.setProperty("--tt-top", rect.top + "px");
  });
})();

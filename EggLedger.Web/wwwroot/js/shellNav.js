(function () {
  if (window.__eggLedgerShellNavInit) return;
  window.__eggLedgerShellNavInit = true;

  const views = new Set(["ships", "drops", "reports"]);

  window.eggLedgerShellNav = {
    readView: function () {
      const hash = (window.location.hash || "").replace(/^#/, "").toLowerCase();
      return views.has(hash) ? hash : "";
    },
    writeView: function (view) {
      if (!views.has(view)) return;
      const target = "/" + window.location.search + "#" + view;
      const current = window.location.pathname + window.location.search + window.location.hash;
      if (current === target) return;
      try {
        window.history.replaceState(window.history.state, "", target);
      } catch {
        window.location.hash = view;
      }
    }
  };
})();

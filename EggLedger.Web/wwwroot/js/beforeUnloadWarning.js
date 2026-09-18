(function () {
  if (window.__beforeUnloadWarningInit) return;
  window.__beforeUnloadWarningInit = true;

  let active = false;

  window.addEventListener("beforeunload", function (e) {
    if (!active) return;
    e.preventDefault();
    e.returnValue = "";
  });

  window.setFetchInProgressWarning = function (isActive) {
    active = isActive;
  };
})();

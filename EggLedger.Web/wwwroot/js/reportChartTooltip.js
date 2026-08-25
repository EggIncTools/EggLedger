(function () {
  if (window.__reportChartTooltipInit) return;
  window.__reportChartTooltipInit = true;

  let el = null;
  let hideTimer = null;

  function ensure() {
    if (el) return el;
    el = document.createElement("div");
    el.className = "tooltip-floating report-tooltip";
    el.style.display = "none";
    document.body.appendChild(el);
    return el;
  }

  function targetWithTT(node) {
    return node?.closest?.("[data-tt]") ?? null;
  }

  function place(box, x, y) {
    const half = box.offsetWidth / 2 + 6;
    x = Math.min(Math.max(x, half), window.innerWidth - half);
    box.classList.toggle("tt-below", y - box.offsetHeight - 14 < 4);
    box.style.left = x + "px";
    box.style.top = y + "px";
  }

  function show(t, x, y) {
    const box = ensure();
    if (hideTimer) {
      clearTimeout(hideTimer);
      hideTimer = null;
    }
    const lines = (t.dataset.tt || "").split("\n");
    box.innerHTML = "";
    lines.forEach(function (line, i) {
      const d = document.createElement("div");
      d.className = i === 0 ? "text-xs text-gray-200 font-medium" : "text-xs text-gray-400";
      d.textContent = line;
      box.appendChild(d);
    });
    box.style.display = "block";
    place(box, x, y);
    requestAnimationFrame(function () { box.classList.add("tt-on"); });
  }

  function hide() {
    if (!el) return;
    el.classList.remove("tt-on");
    if (hideTimer) clearTimeout(hideTimer);
    hideTimer = setTimeout(function () {
      if (el) el.style.display = "none";
    }, 130);
  }

  document.addEventListener("mouseover", function (e) {
    const t = targetWithTT(e.target);
    if (t) show(t, e.clientX, e.clientY);
  });

  document.addEventListener("mousemove", function (e) {
    if (!el || el.style.display === "none") return;
    const t = targetWithTT(e.target);
    if (t) {
      place(el, e.clientX, e.clientY);
    } else {
      hide();
    }
  });

  document.addEventListener("mouseout", function (e) {
    const t = targetWithTT(e.target);
    if (t) hide();
  });
})();

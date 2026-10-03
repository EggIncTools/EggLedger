export function pickTextFile(accept) {
  return new Promise((resolve) => {
    const input = globalThis.document.createElement("input");
    input.type = "file";
    if (accept) input.accept = accept;
    input.style.display = "none";
    input.addEventListener("change", () => {
      const file = input.files && input.files[0];
      input.remove();
      if (!file) { resolve(null); return; }
      const reader = new globalThis.FileReader();
      reader.onload = () => resolve(String(reader.result));
      reader.onerror = () => resolve(null);
      reader.readAsText(file);
    });
    globalThis.document.body.appendChild(input);
    input.click();
  });
}

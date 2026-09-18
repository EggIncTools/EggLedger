export function observe(el, dotNetRef, methodName) {
  const ro = new ResizeObserver(entries => {
    for (const entry of entries) {
      const box = entry.contentBoxSize?.[0];
      const width = box?.inlineSize ?? entry.contentRect.width;
      const height = box?.blockSize ?? entry.contentRect.height;
      dotNetRef.invokeMethodAsync(methodName, width, height);
    }
  });
  ro.observe(el);
  return ro;
}

export function unobserve(observer) {
  observer.disconnect();
}

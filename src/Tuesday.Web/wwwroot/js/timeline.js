// Timeline drag: pointer delta in pixels becomes a whole number of days.
// The Blazor circuit owns the write; this file only measures.

let dotNetRef = null;

export function setHandler(ref) {
  dotNetRef = ref;
}

export function bind() {
  document.querySelectorAll('[data-timeline-track]').forEach((track) => {
    if (track.dataset.bound === '1') {
      return;
    }

    track.dataset.bound = '1';
    track.addEventListener('pointerdown', onPointerDown);
  });
}

function onPointerDown(event) {
  const bar = event.target.closest('[data-item-id]');
  if (!bar) {
    return;
  }

  const track = bar.closest('[data-timeline-track]');
  if (!track) {
    return;
  }

  const span = Number(track.dataset.span || '0');
  const pxPerDay = span > 0 ? track.clientWidth / span : 0;
  if (pxPerDay <= 0) {
    return;
  }

  const startX = event.clientX;
  let days = 0;

  bar.setPointerCapture?.(event.pointerId);
  bar.classList.add('is-dragging');

  const onMove = (moveEvent) => {
    days = Math.round((moveEvent.clientX - startX) / pxPerDay);
    bar.style.transform = `translateX(${days * pxPerDay}px)`;
  };

  const onUp = async () => {
    window.removeEventListener('pointermove', onMove);
    window.removeEventListener('pointerup', onUp);
    bar.classList.remove('is-dragging');
    bar.style.transform = '';

    if (days !== 0 && dotNetRef) {
      await dotNetRef.invokeMethodAsync('ShiftAsync', bar.dataset.itemId, days);
    }
  };

  window.addEventListener('pointermove', onMove);
  window.addEventListener('pointerup', onUp);
}

// Board grid helpers. Escape puts the focused cell back to its last saved value on the client,
// so the person keeps their place (and focus) instead of the whole board being re-rendered.
// The committed value travels on the input as data-committed; the server drops its pending copy
// in the same keystroke.

export function wire(container) {
    if (!container || container.dataset.gridWired) {
        return;
    }

    container.dataset.gridWired = "true";

    container.addEventListener("keydown", (event) => {
        if (event.key !== "Escape") {
            return;
        }

        const input = event.target;
        if (!(input instanceof HTMLInputElement) || !input.hasAttribute("data-committed")) {
            return;
        }

        input.value = input.getAttribute("data-committed") ?? "";
        input.select();
    }, true);
}

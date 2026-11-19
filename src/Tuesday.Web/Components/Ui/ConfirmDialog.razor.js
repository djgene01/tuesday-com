// Opens a <dialog> modally and puts focus back on the control that opened it when the dialog
// closes — by a button, by Escape, or by the server closing it after a confirm.
const triggers = new WeakMap();

export function open(dialog) {
    if (!dialog || dialog.open) {
        return;
    }

    triggers.set(dialog, document.activeElement);

    if (!dialog.dataset.wired) {
        dialog.dataset.wired = "true";
        dialog.addEventListener("close", () => {
            const trigger = triggers.get(dialog);
            triggers.delete(dialog);
            if (trigger && typeof trigger.focus === "function" && trigger.isConnected) {
                trigger.focus();
            }
        });
    }

    dialog.showModal();
}

export function close(dialog) {
    if (dialog && dialog.open) {
        dialog.close();
    }
}

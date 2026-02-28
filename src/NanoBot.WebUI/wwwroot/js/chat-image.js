window.nanoBotChatImage = {
  attachPasteHandler: function (containerElement, dotNetRef) {
    if (!containerElement || !dotNetRef) {
      return;
    }

    const key = "__nanoBotPasteHandler";
    if (containerElement[key]) {
      return;
    }

    const handler = async function (event) {
      try {
        const clipboardData = event.clipboardData;
        if (!clipboardData || !clipboardData.items) {
          return;
        }

        for (let i = 0; i < clipboardData.items.length; i++) {
          const item = clipboardData.items[i];
          if (!item || !item.type || !item.type.startsWith("image/")) {
            continue;
          }

          const file = item.getAsFile();
          if (!file) {
            continue;
          }

          event.preventDefault();

          const dataUrl = await new Promise((resolve, reject) => {
            const reader = new FileReader();
            reader.onload = () => resolve(reader.result);
            reader.onerror = reject;
            reader.readAsDataURL(file);
          });

          if (typeof dataUrl === "string") {
            await dotNetRef.invokeMethodAsync("HandlePastedImageDataUrl", dataUrl, file.type || item.type || "image/png");
          }

          break;
        }
      } catch {
        // ignore paste errors
      }
    };

    containerElement.addEventListener("paste", handler);
    containerElement[key] = handler;
  },

  detachPasteHandler: function (containerElement) {
    if (!containerElement) {
      return;
    }

    const key = "__nanoBotPasteHandler";
    const handler = containerElement[key];
    if (!handler) {
      return;
    }

    containerElement.removeEventListener("paste", handler);
    delete containerElement[key];
  },

  scrollToBottom: function (containerElement) {
    if (!containerElement) {
      return;
    }

    containerElement.scrollTop = containerElement.scrollHeight;
  }
};

(() => {
  const $ = (id) => document.getElementById(id);
  const send = (payload) => window.chrome?.webview?.postMessage(payload);

  const views = { review: $("view-review"), settings: $("view-settings"), help: $("view-help"), write: $("view-write"), chat: $("view-chat") };
  const MAX_CHARS = 10000;
  const states = { loading: $("state-loading"), result: $("state-result"), message: $("state-message") };
  const modeButtons = [...document.querySelectorAll(".mode")];
  const defaultPipeline = "Revisión académica";

  const PILL = {
    ok: ["ok", "Funcionando"],
    limit: ["warn", "Límite alcanzado"],
    invalid: ["bad", "Clave no válida"],
    offline: ["bad", "Sin conexión"],
    missing: ["off", "Sin clave"],
  };

  let current = { text: "", corrected: "", mode: "Correct", hasKeys: false };
  let compact = false;

  function showView(name) {
    for (const [key, el] of Object.entries(views)) el.hidden = key !== name;
    if (name === "settings") $("key-groq").focus();
  }

  function showState(name) {
    for (const [key, el] of Object.entries(states)) el.hidden = key !== name;
    $("btn-apply").disabled = name !== "result";
    $("btn-copy").disabled = name !== "result";
  }

  function setMode(mode) {
    current.mode = mode;
    modeButtons.forEach((b) => {
      const active = b.dataset.mode === mode;
      b.classList.toggle("active", active);
      b.setAttribute("aria-selected", String(active));
    });
  }

  function setLangSwitch(lang) {
    document.querySelectorAll(".lang-switch button").forEach((b) => {
      b.setAttribute("aria-pressed", String(b.dataset.lang === lang));
    });
  }

  document.querySelectorAll(".lang-switch button").forEach((b) =>
    b.addEventListener("click", () => {
      if (b.getAttribute("aria-pressed") === "true") return;
      send({ type: "setLanguage", lang: b.dataset.lang });
    })
  );

  // Semáforo general en la cabecera.
  function setHealthDot(overall) {
    const labels = { green: "APIs funcionando", yellow: "Solo respaldo gratuito disponible", red: "Ninguna API responde", gray: "Comprobando APIs…" };
    const btn = $("health");
    btn.dataset.state = overall;
    btn.title = labels[overall] || labels.gray;
  }

  // Estado por proveedor en la pantalla de claves.
  function renderHealth(msg) {
    setHealthDot(msg.overall);
    for (const p of msg.providers) {
      const key = p.name.toLowerCase();
      const pill = document.querySelector(`[data-pill="${key}"]`);
      if (pill) {
        const [cls, text] = PILL[p.state] || PILL.offline;
        pill.className = `pill ${cls}`;
        pill.textContent = text;
      }
      const detail = document.querySelector(`[data-detail="${key}"]`);
      if (detail && p.detail) detail.textContent = p.detail;
    }
    for (const [name, mask] of Object.entries(msg.masks || {})) {
      const el = document.querySelector(`[data-mask="${name}"]`);
      if (el) el.textContent = mask ? `Guardada: ${mask}` : "sin clave guardada";
    }
  }

  function renderDiff(segments) {
    const box = $("diff");
    box.replaceChildren();
    for (const seg of segments) {
      if (seg.kind === "equal") {
        box.append(document.createTextNode(seg.text));
      } else {
        const span = document.createElement("span");
        span.className = seg.kind === "delete" ? "del" : "ins";
        span.textContent = seg.text;
        box.append(span);
      }
    }
  }

  function pairNode(c) {
    const pair = document.createElement("div");
    pair.className = "pair";
    const from = document.createElement("s");
    from.textContent = c.original;
    const to = document.createElement("b");
    to.textContent = c.replacement;
    pair.append(from, document.createTextNode("  →  "), to);
    return pair;
  }

  function renderChanges(changes, mode) {
    const list = $("changes");
    list.replaceChildren();
    const translating = mode === "Translate";
    $("changes-actions").hidden = translating || changes.length === 0;
    if (!changes.length) {
      $("count").textContent = "";
      const li = document.createElement("li");
      li.className = "why";
      li.textContent = "No se encontraron cambios. El texto ya está en buen estado.";
      list.append(li);
      return;
    }

    if (translating) {
      $("count").textContent = "";
      for (const c of changes) {
        const li = document.createElement("li");
        const why = document.createElement("div");
        why.className = "why";
        why.textContent = c.reason;
        li.append(pairNode(c), why);
        list.append(li);
      }
      return;
    }

    const accepted = changes.filter((c) => c.selected && c.applicable).length;
    $("count").textContent = `(${accepted} de ${changes.length} aceptados)`;

    const groups = new Map();
    changes.forEach((c, index) => {
      const key = c.applicable ? c.sentence : "__none__";
      if (!groups.has(key)) groups.set(key, []);
      groups.get(key).push({ c, index });
    });

    for (const [sentence, items] of groups) {
      const li = document.createElement("li");
      li.className = "group";
      if (sentence !== "__none__") {
        const head = document.createElement("div");
        head.className = "sent";
        head.textContent = sentence;
        li.append(head);
      }
      for (const { c, index } of items) {
        const row = document.createElement("label");
        row.className = "row" + (c.applicable ? "" : " muted");
        const box = document.createElement("input");
        box.type = "checkbox";
        box.checked = c.selected;
        box.disabled = !c.applicable;
        box.addEventListener("change", () => send({ type: "toggle", index, on: box.checked }));
        const text = document.createElement("div");
        text.append(pairNode(c));
        const why = document.createElement("div");
        why.className = "why";
        why.textContent = c.applicable ? c.reason : "No se pudo ubicar este cambio en el texto.";
        text.append(why);
        row.append(box, text);
        li.append(row);
      }
      list.append(li);
    }
  }

  function showMessage(text) {
    $("message").textContent = text;
    showView("review");
    showState("message");
  }

  function handle(msg) {
    switch (msg.type) {
      case "open":
        current.text = msg.text;
        current.hasKeys = msg.hasGroq || msg.hasGemini;
        setLangSwitch(msg.lang);
        showView("review");
        setMode("Correct");
        showState("loading");
        break;

      case "loading":
        setMode(msg.mode);
        $("pipeline").textContent = "Revisando…";
        showView("review");
        showState("loading");
        break;

      case "result":
        current.corrected = msg.corrected;
        $("pipeline").textContent = msg.pipeline;
        setMode(msg.mode);
        if (msg.mode === "Translate") {
          $("diff").replaceChildren(document.createTextNode(msg.corrected));
          $("result-label").textContent = "Traducción";
          $("changes-label").textContent = "Notas de traducción";
        } else {
          renderDiff(msg.segments);
          $("result-label").textContent = "Propuesta";
          $("changes-label").textContent = "Cambios";
        }
        renderChanges(msg.changes, msg.mode);
        showView("review");
        showState("result");
        break;

      case "notice":
      case "error":
        showMessage(msg.message);
        break;

      case "settings":
        showView("settings");
        setLangSwitch(msg.lang);
        $("pipeline").textContent = "Claves de API";
        break;

      case "health":
        renderHealth(msg);
        break;

      case "saved": {
        const hint = $("saved");
        hint.hidden = false;
        if (msg.ok) {
          hint.style.color = "";
          hint.textContent = "Claves guardadas. No volverán a pedirse.";
          for (const id of ["key-groq", "key-gemini", "key-nvidia"]) $(id).value = "";
        } else {
          hint.style.color = "var(--del-ink)";
          hint.textContent = msg.message;
        }
        setTimeout(() => (hint.hidden = true), 3500);
        break;
      }

      case "compact":
        setCompact(msg.on);
        break;

      case "help":
        openView("help");
        break;

      case "chat":
        openView("chat");
        $("chat-input").focus();
        break;

      case "chatBusy":
        addChatBubble("pending", "Pensando…");
        break;

      case "chatReply":
        removePending();
        addChatBubble("assistant", msg.text, msg.provider);
        break;

      case "chatError":
        removePending();
        addChatBubble("error", msg.message);
        break;

      case "write":
        openView("write");
        updateCounter();
        $("input-text").focus();
        break;

      case "applied-local":
        $("input-text").value = msg.text;
        openView("write");
        updateCounter();
        setInputStatus("Copiado al portapapeles.");
        break;

      case "language":
        setLangSwitch(msg.lang);
        break;
    }
  }

  function openView(name) {
    showView(name);
    $("pipeline").textContent = name === "help" ? "Ayuda" : name === "write" ? "Texto propio" : defaultPipeline;
  }

  function removePending() {
    document.querySelectorAll(".bubble.pending").forEach((el) => el.remove());
  }

  function addChatBubble(kind, text, provider) {
    const log = $("chat-log");
    const wrap = document.createElement("div");
    wrap.className = `bubble ${kind}`;
    const body = document.createElement("div");
    body.className = "bubble-text";
    body.textContent = text;
    wrap.append(body);
    if (kind === "assistant") {
      const meta = document.createElement("div");
      meta.className = "bubble-meta";
      const copy = document.createElement("button");
      copy.className = "link";
      copy.textContent = "Copiar";
      copy.addEventListener("click", () => send({ type: "copy", text }));
      meta.append(copy);
      if (provider) {
        const who = document.createElement("span");
        who.textContent = provider;
        meta.append(who);
      }
      wrap.append(meta);
    }
    log.append(wrap);
    log.scrollTop = log.scrollHeight;
  }

  function sendChat() {
    const input = $("chat-input");
    const text = input.value.trim();
    if (!text) return;
    addChatBubble("user", text);
    input.value = "";
    send({ type: "chatSend", text });
  }

  $("btn-chat").addEventListener("click", () => {
    send({ type: "openChat" });
  });
  $("btn-chat-send").addEventListener("click", sendChat);
  $("chat-input").addEventListener("keydown", (e) => {
    if (e.key === "Enter" && !e.shiftKey) {
      e.preventDefault();
      sendChat();
    }
  });
  $("btn-chat-new").addEventListener("click", () => {
    $("chat-log").replaceChildren();
    send({ type: "chatClear" });
  });

  function setInputStatus(text) {
    $("input-status").textContent = text;
  }

  function updateCounter() {
    const value = $("input-text").value;
    const n = value.length;
    const over = n > MAX_CHARS;
    $("input-counter").textContent = `${n.toLocaleString("es-MX").replace(/,/g, " ")} / ${MAX_CHARS.toLocaleString("es-MX").replace(/,/g, " ")}`;
    $("input-counter").classList.toggle("over", over);
    $("btn-write-review").disabled = over || !value.trim();
    if (over) setInputStatus(`Supera el límite. Recorta ${n - MAX_CHARS} caracteres.`);
    else if (!value.trim()) setInputStatus("");
    else setInputStatus("");
  }

  function submitText() {
    const value = $("input-text").value;
    if (!value.trim() || value.length > MAX_CHARS) return;
    current.text = value;
    send({ type: "reviewText", text: value });
  }

  $("btn-write").addEventListener("click", () => openView("write"));
  $("btn-write-cancel").addEventListener("click", closeSubview);
  $("btn-write-review").addEventListener("click", submitText);
  $("input-text").addEventListener("input", updateCounter);

  function closeSubview() {
    showView("review");
    $("pipeline").textContent = defaultPipeline;
  }

  const BASE_W = 392;
  const BASE_H = 500;

  function applyZoom() {
    const z = compact ? 1 : Math.min(window.innerWidth / BASE_W, window.innerHeight / BASE_H);
    document.documentElement.style.setProperty("--z", String(z));
  }

  function setCompact(on) {
    compact = on;
    document.body.classList.toggle("compact", on);
    $("sheet").classList.toggle("compact", on);
    $("expand").hidden = !on;
    $("views").hidden = on;
    $("drag").hidden = on;
    applyZoom();
  }

  window.addEventListener("resize", applyZoom);
  applyZoom();

  modeButtons.forEach((b) =>
    b.addEventListener("click", () => {
      if (!current.text) return;
      send({ type: "run", mode: b.dataset.mode });
    })
  );

  $("btn-apply").addEventListener("click", () => {
    if (current.corrected) send({ type: "apply", text: current.corrected });
  });
  $("btn-copy").addEventListener("click", () => {
    if (current.corrected) send({ type: "copy", text: current.corrected });
  });
  $("btn-discard").addEventListener("click", () => send({ type: "close" }));
  $("btn-close").addEventListener("click", () => send({ type: "close" }));
  $("btn-minimize").addEventListener("click", () => send({ type: "collapse", on: true }));
  $("btn-help").addEventListener("click", () => openView("help"));
  $("btn-help-back").addEventListener("click", closeSubview);
  $("btn-settings").addEventListener("click", () => openView("settings"));
  $("health").addEventListener("click", () => openView("settings"));
  $("btn-back").addEventListener("click", closeSubview);
  $("btn-test").addEventListener("click", () => send({ type: "checkKeys" }));
  $("btn-accept-all").addEventListener("click", () => send({ type: "setAll", on: true }));
  $("btn-reject-all").addEventListener("click", () => send({ type: "setAll", on: false }));
  $("btn-save").addEventListener("click", () =>
    send({ type: "saveKeys", groq: $("key-groq").value, gemini: $("key-gemini").value, nvidia: $("key-nvidia").value })
  );

  // Arrastre de la ventana desde la barra superior.
  const drag = $("drag");
  let dragging = false;
  let last = { x: 0, y: 0 };
  drag.addEventListener("pointerdown", (e) => {
    if (e.target.closest("button")) return;
    dragging = true;
    last = { x: e.screenX, y: e.screenY };
    drag.setPointerCapture(e.pointerId);
  });
  drag.addEventListener("pointermove", (e) => {
    if (!dragging) return;
    const dx = e.screenX - last.x;
    const dy = e.screenY - last.y;
    last = { x: e.screenX, y: e.screenY };
    if (dx || dy) send({ type: "move", dx, dy });
  });
  const endDrag = () => (dragging = false);
  drag.addEventListener("pointerup", endDrag);
  drag.addEventListener("pointercancel", endDrag);

  // Esquina inferior derecha: cambiar tamaño.
  const grip = $("grip");
  let resizing = false;
  let lastGrip = { x: 0, y: 0 };
  grip.addEventListener("pointerdown", (e) => {
    resizing = true;
    lastGrip = { x: e.screenX, y: e.screenY };
    grip.setPointerCapture(e.pointerId);
  });
  grip.addEventListener("pointermove", (e) => {
    if (!resizing) return;
    const dx = e.screenX - lastGrip.x;
    const dy = e.screenY - lastGrip.y;
    lastGrip = { x: e.screenX, y: e.screenY };
    if (dx || dy) send({ type: "resize", dx, dy });
  });
  const endGrip = () => (resizing = false);
  grip.addEventListener("pointerup", endGrip);
  grip.addEventListener("pointercancel", endGrip);

  // El icono minimizado se arrastra libremente; un clic sin movimiento lo vuelve a abrir.
  const expandBtn = $("expand");
  let compactDrag = null;
  expandBtn.addEventListener("pointerdown", (e) => {
    compactDrag = { x: e.screenX, y: e.screenY, moved: false };
    expandBtn.setPointerCapture(e.pointerId);
  });
  expandBtn.addEventListener("pointermove", (e) => {
    if (!compactDrag) return;
    const dx = e.screenX - compactDrag.x;
    const dy = e.screenY - compactDrag.y;
    compactDrag.x = e.screenX;
    compactDrag.y = e.screenY;
    if (dx || dy) compactDrag.moved = true;
    if (compactDrag.moved && (dx || dy)) send({ type: "move", dx, dy });
  });
  expandBtn.addEventListener("pointerup", () => {
    if (compactDrag && !compactDrag.moved) send({ type: "collapse", on: false });
    compactDrag = null;
  });
  expandBtn.addEventListener("pointercancel", () => (compactDrag = null));

  document.addEventListener("keydown", (e) => {
    if (e.key === "Escape") {
      if (!views.help.hidden || !views.settings.hidden || !views.write.hidden || !views.chat.hidden) closeSubview();
      else send({ type: "close" });
    } else if (e.ctrlKey && e.key === "Enter") {
      if (!views.write.hidden) submitText();
      else if (current.corrected) send({ type: "apply", text: current.corrected });
    }
  });

  window.chrome?.webview?.addEventListener("message", (e) => handle(e.data));
  send({ type: "ready" });
})();

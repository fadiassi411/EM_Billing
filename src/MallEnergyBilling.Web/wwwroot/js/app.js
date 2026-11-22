// Watchdog Energy Management 4.4 — interface behaviour.
// - Sidebar toggle on small screens
// - Status pills coloured from their text
// - Data grids: any <table data-grid="name"> gets instant search, status filter chips,
//   sortable columns and pages. The view is remembered per page while the tab is open.
// - Live refresh: elements with data-live and an id are refreshed in place every few
//   seconds instead of reloading the whole page (so searches and scroll are kept).
(() => {
  "use strict";

  const store = {
    get(key) { try { return JSON.parse(sessionStorage.getItem(key) || "null"); } catch { return null; } },
    set(key, value) { try { sessionStorage.setItem(key, JSON.stringify(value)); } catch { /* storage unavailable */ } }
  };
  const normalise = text => (text || "").toLocaleLowerCase().normalize("NFD").replace(/[̀-ͯ]/g, "").replace(/\s+/g, " ").trim();

  // ---------- sidebar ----------
  function setupSidebar() {
    const root = document.documentElement;
    document.querySelectorAll("[data-menu-toggle]").forEach(button => button.addEventListener("click", () => {
      const open = !root.classList.contains("nav-open");
      root.classList.toggle("nav-open", open);
      button.setAttribute("aria-expanded", String(open));
    }));
    document.querySelectorAll("[data-menu-close]").forEach(el => el.addEventListener("click", () => root.classList.remove("nav-open")));
    document.addEventListener("keydown", e => { if (e.key === "Escape") root.classList.remove("nav-open"); });
    document.addEventListener("click", e => {
      document.querySelectorAll("details.user-menu[open]").forEach(menu => { if (!menu.contains(e.target)) menu.removeAttribute("open"); });
    });
  }

  // ---------- status tones ----------
  const tones = [
    ["bad", /(fail|error|timeout|stale|frozen|overdue|counter review|offline|invalid|expired|bad|disconnected|cancel)/],
    ["warn", /(partial|awaiting|not commissioned|review|draft|pending|waiting|scheduled|not read|partially)/],
    ["ok", /(online|connected|paid|good|licensed|completed|active|ready|success)/],
    ["info", /(finalized|published|calculated|closed)/],
    ["muted", /(disabled|archived|inactive|vacant|unassigned|free)/]
  ];
  function toneOf(text) {
    const value = normalise(text);
    for (const [tone, pattern] of tones) if (pattern.test(value)) return tone;
    return "muted";
  }
  function colourStatuses(root = document) {
    root.querySelectorAll(".status").forEach(pill => {
      pill.classList.remove("tone-ok", "tone-bad", "tone-warn", "tone-info", "tone-muted");
      pill.classList.add("tone-" + toneOf(pill.textContent));
    });
  }

  // ---------- data grid ----------
  const grids = [];
  const searchIcon = '<svg viewBox="0 0 24 24" aria-hidden="true"><circle cx="11" cy="11" r="7"/><path d="m20 20-3.5-3.5"/></svg>';

  function cellValue(row, index) {
    const cell = row.cells[index];
    if (!cell) return "";
    return (cell.dataset.sort ?? cell.textContent).trim();
  }
  function compare(a, b) {
    // "USD 1,234.50", "1,100.000 kWh", "45 %" sort as numbers; anything else as text or date.
    const number = v => {
      const t = v.replace(/\b[A-Z]{3}\b|kWh|m³|%|\s/g, "");
      return /^-?[\d,]*\.?\d+$/.test(t) ? parseFloat(t.replace(/,/g, "")) : NaN;
    };
    const na = number(a), nb = number(b);
    if (!isNaN(na) && !isNaN(nb)) return na - nb;
    const da = Date.parse(a), db = Date.parse(b);
    if (!isNaN(da) && !isNaN(db) && /\d{4}/.test(a) && /\d{4}/.test(b)) return da - db;
    return a.localeCompare(b, undefined, { numeric: true, sensitivity: "base" });
  }

  function setupGrid(table) {
    if (table.dataset.gridReady) return;
    table.dataset.gridReady = "1";
    const name = table.dataset.grid || "table";
    const key = "grid:" + location.pathname + ":" + name;
    const defaults = { q: table.dataset.gridQuery || "", status: "", size: Number(table.dataset.gridSize || 25), page: 1, sort: -1, dir: 1 };
    const state = Object.assign({}, defaults, store.get(key) || {});
    if (table.dataset.gridQuery) state.q = table.dataset.gridQuery;
    const body = table.tBodies[0];
    if (!body) return;
    const placeholderText = table.dataset.gridEmpty || "No matching rows.";
    const statusEnabled = table.dataset.gridStatus !== "off";

    const toolbar = document.createElement("div");
    toolbar.className = "grid-toolbar";
    toolbar.innerHTML =
      `<label class="grid-search">${searchIcon}<input type="search" placeholder="${table.dataset.gridPlaceholder || "Search…"}" aria-label="Search this list" autocomplete="off"></label>` +
      `<div class="grid-chips" role="group" aria-label="Filter by status"></div>` +
      `<label class="grid-size">Rows <select aria-label="Rows per page"><option>10</option><option>25</option><option>50</option><option>100</option><option value="0">All</option></select></label>`;
    const footer = document.createElement("div");
    footer.className = "grid-footer";
    footer.innerHTML = `<span class="grid-count" aria-live="polite"></span><div class="grid-pager"></div>`;
    const anchor = table.closest(".table-responsive") || table;
    anchor.before(toolbar);
    anchor.after(footer);

    const search = toolbar.querySelector("input");
    const chips = toolbar.querySelector(".grid-chips");
    const size = toolbar.querySelector("select");
    const count = footer.querySelector(".grid-count");
    const pager = footer.querySelector(".grid-pager");
    search.value = state.q;
    size.value = String(state.size);
    if (![...size.options].some(o => o.value === String(state.size))) { state.size = 25; size.value = "25"; }

    const headers = table.tHead ? [...table.tHead.rows[0].cells] : [];
    headers.forEach((th, index) => {
      if (!th.textContent.trim() || th.dataset.sort === "off") return;
      th.classList.add("sortable");
      th.tabIndex = 0;
      th.setAttribute("role", "button");
      const sortBy = () => {
        if (state.sort === index) state.dir = -state.dir; else { state.sort = index; state.dir = 1; }
        state.page = 1; render();
      };
      th.addEventListener("click", sortBy);
      th.addEventListener("keydown", e => { if (e.key === "Enter" || e.key === " ") { e.preventDefault(); sortBy(); } });
    });

    let timer;
    search.addEventListener("input", () => { clearTimeout(timer); timer = setTimeout(() => { state.q = search.value; state.page = 1; render(); }, 120); });
    size.addEventListener("change", () => { state.size = Number(size.value); state.page = 1; render(); });

    function dataRows() {
      return [...body.rows].filter(row => !row.classList.contains("grid-empty") && !(row.cells.length === 1 && row.cells[0].colSpan > 1));
    }

    function render() {
      const rows = dataRows();
      // Server-rendered "nothing here" rows are only shown when the table really is empty.
      [...body.rows].filter(r => r.cells.length === 1 && r.cells[0].colSpan > 1 && !r.classList.contains("grid-empty")).forEach(r => r.hidden = rows.length > 0);
      const statusOf = row => statusEnabled ? (row.querySelector(".status")?.textContent.trim() || "") : "";

      // status chips
      const totals = new Map();
      rows.forEach(row => { const s = statusOf(row); if (s) totals.set(s, (totals.get(s) || 0) + 1); });
      if (state.status && !totals.has(state.status)) state.status = "";
      chips.innerHTML = "";
      if (totals.size > 1 || (totals.size === 1 && rows.length > 10)) {
        const chip = (label, value, n) => {
          const button = document.createElement("button");
          button.type = "button";
          button.className = "grid-chip" + (state.status === value ? " active" : "") + (value ? " tone-" + toneOf(value) : "");
          button.innerHTML = `${label}<b>${n}</b>`;
          button.setAttribute("aria-pressed", String(state.status === value));
          button.addEventListener("click", () => { state.status = state.status === value ? "" : value; state.page = 1; render(); });
          chips.appendChild(button);
        };
        chip("All", "", rows.length);
        [...totals.entries()].sort((a, b) => b[1] - a[1]).forEach(([s, n]) => chip(s, s, n));
      }

      // filter
      const terms = normalise(state.q).split(" ").filter(Boolean);
      const matches = rows.filter(row => {
        if (state.status && statusOf(row) !== state.status) return false;
        if (!terms.length) return true;
        const text = normalise(row.dataset.search ?? row.textContent);
        return terms.every(t => text.includes(t));
      });

      // sort
      headers.forEach((th, i) => th.classList.toggle("sort-asc", state.sort === i && state.dir === 1));
      headers.forEach((th, i) => th.classList.toggle("sort-desc", state.sort === i && state.dir === -1));
      if (state.sort >= 0) {
        matches.sort((a, b) => compare(cellValue(a, state.sort), cellValue(b, state.sort)) * state.dir);
        matches.forEach(row => body.appendChild(row));
      }

      // pages
      const pageSize = state.size > 0 ? state.size : matches.length || 1;
      const pages = Math.max(1, Math.ceil(matches.length / pageSize));
      state.page = Math.min(Math.max(1, state.page), pages);
      const start = (state.page - 1) * pageSize;
      const visible = new Set(matches.slice(start, start + pageSize));
      rows.forEach(row => row.hidden = !visible.has(row));

      let empty = body.querySelector("tr.grid-empty");
      if (rows.length > 0 && matches.length === 0) {
        if (!empty) {
          empty = document.createElement("tr");
          empty.className = "grid-empty";
          empty.innerHTML = `<td colspan="${headers.length || 1}">${placeholderText}</td>`;
          body.appendChild(empty);
        }
      } else empty?.remove();

      const from = matches.length ? start + 1 : 0, to = Math.min(start + pageSize, matches.length);
      count.textContent = rows.length === 0 ? "" : matches.length === rows.length
        ? `Showing ${from}–${to} of ${rows.length}`
        : `Showing ${from}–${to} of ${matches.length} (filtered from ${rows.length})`;

      pager.innerHTML = "";
      if (pages > 1) {
        const add = (label, page, opts = {}) => {
          const b = document.createElement("button");
          b.type = "button"; b.textContent = label;
          if (opts.active) b.className = "active";
          if (opts.disabled) b.disabled = true;
          if (opts.aria) b.setAttribute("aria-label", opts.aria);
          b.addEventListener("click", () => { state.page = page; render(); toolbar.scrollIntoView({ block: "nearest", behavior: "smooth" }); });
          pager.appendChild(b);
        };
        add("‹", state.page - 1, { disabled: state.page === 1, aria: "Previous page" });
        const shown = new Set([1, pages, state.page - 1, state.page, state.page + 1].filter(p => p >= 1 && p <= pages));
        let last = 0;
        [...shown].sort((a, b) => a - b).forEach(p => {
          if (p - last > 1) { const gap = document.createElement("span"); gap.className = "gap"; gap.textContent = "…"; pager.appendChild(gap); }
          add(String(p), p, { active: p === state.page });
          last = p;
        });
        add("›", state.page + 1, { disabled: state.page === pages, aria: "Next page" });
      }
      footer.hidden = rows.length === 0;
      toolbar.hidden = rows.length === 0 && !state.q;
      if (!table.dataset.gridQuery) store.set(key, state);
      else store.set(key, Object.assign({}, state, { q: "" }));
    }

    grids.push({ table, render });
    render();
  }

  // ---------- meter card board ----------
  // <section data-board="name"> with [data-board-items] holding [data-card] tiles (data-kind,
  // data-status, data-search). Type pills, status chips, search and pages, plus a Cards/List switch.
  function setupBoard(board) {
    const key = "board:" + location.pathname + ":" + board.dataset.board;
    const state = Object.assign({ kind: "", status: "", q: "", page: 1 }, store.get(key) || {});
    const pageSize = Number(board.dataset.boardSize || 36);
    const items = board.querySelector("[data-board-items]");
    const search = board.querySelector(".board-search input");
    const chips = board.querySelector(".board-chips");
    const pills = [...board.querySelectorAll(".kind-pills [data-kind]")];
    const count = board.querySelector(".board-count");
    const pager = board.querySelector(".board-pager");
    if (!items) return;
    if (search) search.value = state.q;
    if (!pills.some(p => p.dataset.kind === state.kind)) state.kind = "";

    // Cards / List switch, remembered on this computer.
    const viewKey = "view:" + location.pathname + ":" + board.dataset.board;
    const setView = view => {
      board.querySelectorAll("[data-view]").forEach(b => b.setAttribute("aria-pressed", String(b.dataset.view === view)));
      board.querySelectorAll("[data-view-panel]").forEach(p => p.hidden = p.dataset.viewPanel !== view);
      try { localStorage.setItem(viewKey, view); } catch { /* storage unavailable */ }
    };
    board.querySelectorAll("[data-view]").forEach(b => b.addEventListener("click", () => setView(b.dataset.view)));
    let savedView = "cards";
    try { savedView = localStorage.getItem(viewKey) || "cards"; } catch { /* storage unavailable */ }
    setView(savedView);

    pills.forEach(p => p.addEventListener("click", () => { state.kind = p.dataset.kind; state.status = ""; state.page = 1; render(); }));
    let timer;
    search?.addEventListener("input", () => { clearTimeout(timer); timer = setTimeout(() => { state.q = search.value; state.page = 1; render(); }, 120); });

    function render() {
      const cards = [...items.querySelectorAll("[data-card]")];
      pills.forEach(p => p.classList.toggle("active", p.dataset.kind === state.kind));
      const ofKind = cards.filter(c => !state.kind || c.dataset.kind === state.kind);

      const totals = new Map();
      ofKind.forEach(c => totals.set(c.dataset.status, (totals.get(c.dataset.status) || 0) + 1));
      if (state.status && !totals.has(state.status)) state.status = "";
      chips.innerHTML = "";
      if (totals.size > 1) {
        const chip = (label, value, n) => {
          const b = document.createElement("button");
          b.type = "button";
          b.className = "grid-chip" + (state.status === value ? " active" : "") + (value ? " tone-" + toneOf(value) : "");
          b.innerHTML = `${label}<b>${n}</b>`;
          b.setAttribute("aria-pressed", String(state.status === value));
          b.addEventListener("click", () => { state.status = state.status === value ? "" : value; state.page = 1; render(); });
          chips.appendChild(b);
        };
        chip("All", "", ofKind.length);
        [...totals.entries()].sort((a, b) => b[1] - a[1]).forEach(([s, n]) => chip(s, s, n));
      }

      const terms = normalise(state.q).split(" ").filter(Boolean);
      const matches = ofKind.filter(c => (!state.status || c.dataset.status === state.status)
        && terms.every(t => normalise(c.dataset.search).includes(t)));
      const pages = Math.max(1, Math.ceil(matches.length / pageSize));
      state.page = Math.min(Math.max(1, state.page), pages);
      const start = (state.page - 1) * pageSize;
      const visible = new Set(matches.slice(start, start + pageSize));
      cards.forEach(c => c.hidden = !visible.has(c));

      let empty = items.querySelector(".board-empty.filtered");
      if (cards.length && !matches.length) {
        if (!empty) { empty = document.createElement("div"); empty.className = "board-empty filtered"; empty.textContent = "No meter matches this filter."; items.appendChild(empty); }
      } else empty?.remove();

      if (count) count.textContent = cards.length === 0 ? "" : `Showing ${matches.length ? start + 1 : 0}–${Math.min(start + pageSize, matches.length)} of ${matches.length} meter(s)` + (matches.length !== cards.length ? ` (of ${cards.length})` : "");
      if (pager) {
        pager.innerHTML = "";
        if (pages > 1) for (let p = 1; p <= pages; p++) {
          const b = document.createElement("button");
          b.type = "button"; b.textContent = String(p);
          if (p === state.page) b.className = "active";
          b.addEventListener("click", () => { state.page = p; render(); board.scrollIntoView({ block: "start", behavior: "smooth" }); });
          pager.appendChild(b);
        }
      }
      store.set(key, state);
    }
    grids.push({ table: board, render });
    render();
  }

  // ---------- live refresh ----------
  function setupLiveRefresh() {
    const host = document.querySelector("[data-live-refresh]");
    if (!host) return;
    const seconds = Math.max(5, Number(host.dataset.liveRefresh) || 10);
    const indicator = document.querySelector("[data-live-indicator]");
    let busy = false;
    setInterval(async () => {
      if (busy || document.hidden) return;
      const active = document.activeElement;
      if (active && active.closest("[data-live]") && active.matches("input, select, textarea")) return;
      busy = true;
      try {
        const response = await fetch(location.href, { credentials: "same-origin", headers: { "X-Requested-With": "live-refresh" } });
        if (!response.ok || response.redirected) throw new Error("refresh failed");
        const doc = new DOMParser().parseFromString(await response.text(), "text/html");
        document.querySelectorAll("[data-live][id]").forEach(el => {
          const fresh = doc.getElementById(el.id);
          if (fresh && fresh.innerHTML !== el.innerHTML) el.innerHTML = fresh.innerHTML;
        });
        colourStatuses();
        grids.forEach(g => g.render());
        indicator?.classList.remove("stale");
        if (indicator) indicator.textContent = "Live · updated " + new Date().toLocaleTimeString([], { hour: "2-digit", minute: "2-digit", second: "2-digit" });
      } catch {
        indicator?.classList.add("stale");
        if (indicator) indicator.textContent = "Live updates paused";
      } finally { busy = false; }
    }, seconds * 1000);
  }

  // ---------- clock ----------
  function setupClock() {
    const clock = document.querySelector("[data-clock]");
    if (!clock) return;
    const tick = () => clock.textContent = new Date().toLocaleString([], { weekday: "short", day: "2-digit", month: "short", year: "numeric", hour: "2-digit", minute: "2-digit" });
    tick(); setInterval(tick, 15000);
  }

  // ---------- one submission per click ----------
  // A second click (or Enter) while a form is being sent would repeat the action, e.g. delete
  // a meter that is already deleted. After a confirmed submit the form's buttons are locked
  // until the next page loads; they unlock after 15 s in case the request never completes.
  function setupSingleSubmit() {
    document.addEventListener("submit", e => {
      const form = e.target;
      if (!(form instanceof HTMLFormElement) || e.defaultPrevented || (form.method || "").toLowerCase() !== "post") return;
      if (form.dataset.submitting) { e.preventDefault(); return; }
      form.dataset.submitting = "1";
      const buttons = [...form.querySelectorAll("button, input[type=submit]")];
      setTimeout(() => buttons.forEach(b => { b.disabled = true; b.setAttribute("aria-busy", "true"); }), 0);
      setTimeout(() => { delete form.dataset.submitting; buttons.forEach(b => { b.disabled = false; b.removeAttribute("aria-busy"); }); }, 15000);
    });
    // Coming back with the browser Back button restores the page from cache: unlock it.
    window.addEventListener("pageshow", e => {
      if (!e.persisted) return;
      document.querySelectorAll("form[data-submitting]").forEach(f => {
        delete f.dataset.submitting;
        f.querySelectorAll("button, input[type=submit]").forEach(b => { b.disabled = false; b.removeAttribute("aria-busy"); });
      });
    });
  }

  document.addEventListener("DOMContentLoaded", () => {
    setupSingleSubmit();
    setupSidebar();
    colourStatuses();
    document.querySelectorAll("table[data-grid]").forEach(setupGrid);
    document.querySelectorAll("[data-board]").forEach(setupBoard);
    setupLiveRefresh();
    setupClock();
    document.addEventListener("keydown", e => {
      if (e.key === "/" && !e.target.closest("input, textarea, select, [contenteditable]")) {
        const box = document.querySelector(".grid-search input");
        if (box) { e.preventDefault(); box.focus(); }
      }
    });
  });
})();

(() => {
  "use strict";

  const LIMIT = 500;
  const doc = document;
  const root = doc.documentElement;
  const collator = new Intl.Collator(undefined, { numeric: true, sensitivity: "base" });
  const textOf = new WeakMap();
  const themeKey = "silentscan-report-theme";
  const themes = ["auto", "light", "dark"];
  const globalInput = doc.getElementById("q");
  const status = doc.getElementById("qs");
  let globalQuery = "";
  let savedOpen = null;

  const debounce = (fn, wait) => {
    let timer = 0;
    return (...args) => {
      clearTimeout(timer);
      timer = setTimeout(() => fn(...args), wait);
    };
  };

  const plural = (n, one, many) => `${n.toLocaleString()} ${n === 1 ? one : many}`;

  const rowText = (row) => {
    let text = textOf.get(row);
    if (text === undefined) {
      text = row.textContent.toLowerCase();
      textOf.set(row, text);
    }
    return text;
  };

  const makeTable = (el) => {
    const table = el.querySelector("table");
    const state = {
      el,
      table,
      tbody: table.tBodies[0],
      rows: null,
      local: "",
      headingMatch: false,
      showAll: false,
      capped: table.classList.contains("cap"),
      dirty: false,
      total: table.tBodies[0].rows.length,
      count: el.querySelector(".cnt"),
      more: el.querySelector(".more"),
      note: el.querySelector(".capnote"),
      sortColumn: -1,
      sortDirection: 1,
    };
    return state;
  };

  const rowsOf = (t) => {
    if (t.rows === null) {
      t.rows = Array.from(t.tbody.rows);
    }
    return t.rows;
  };

  const setCount = (t, matched, shown) => {
    if (t.count) {
      t.count.textContent = matched === t.total ? plural(t.total, "row", "rows") : `${matched.toLocaleString()} of ${plural(t.total, "row", "rows")}`;
    }
    const hiddenMatches = matched > shown;
    if (t.more) {
      t.more.hidden = !hiddenMatches;
      t.more.textContent = matched === t.total ? `Show all ${plural(t.total, "row", "rows")}` : `Show all ${plural(matched, "match", "matches")}`;
    }
    if (t.note) {
      t.note.hidden = !hiddenMatches;
      t.note.textContent = `first ${shown.toLocaleString()} shown`;
    }
  };

  const resetTable = (t) => {
    if (t.dirty) {
      for (const row of rowsOf(t)) {
        row.hidden = false;
      }
      t.dirty = false;
    }
    const capActive = t.capped && !t.showAll;
    t.table.classList.toggle("cap", capActive);
    setCount(t, t.total, capActive ? Math.min(LIMIT, t.total) : t.total);
    return t.total;
  };

  const applyTable = (t) => {
    const local = t.local;
    const global = t.headingMatch ? "" : globalQuery;
    if (!local && !global) {
      return resetTable(t);
    }
    t.table.classList.remove("cap");
    let matched = 0;
    let shown = 0;
    for (const row of rowsOf(t)) {
      const text = rowText(row);
      const ok = (!local || text.includes(local)) && (!global || text.includes(global));
      let visible = false;
      if (ok) {
        matched += 1;
        visible = t.showAll || shown < LIMIT;
      }
      if (visible) {
        shown += 1;
      }
      row.hidden = !visible;
    }
    t.dirty = true;
    setCount(t, matched, shown);
    return matched;
  };

  const tables = new Map();
  for (const el of doc.querySelectorAll(".tbl")) {
    tables.set(el, makeTable(el));
  }

  const tocLinks = new Map();
  for (const link of doc.querySelectorAll(".toc a[href^='#']")) {
    tocLinks.set(link.getAttribute("href").slice(1), link);
  }

  const sections = Array.from(doc.querySelectorAll("section.sec"), (el) => ({
    el,
    details: el.querySelector("details"),
    head: el.querySelector("h2").textContent.toLowerCase(),
    body: null,
    tables: Array.from(el.querySelectorAll(".tbl"), (t) => tables.get(t)),
    toc: tocLinks.get(el.id)?.closest("li") ?? null,
  }));

  const bodyText = (s) => {
    if (s.body === null) {
      s.body = s.el.textContent.toLowerCase();
    }
    return s.body;
  };

  const sectionMatches = (s, query) => {
    const headingMatch = s.head.includes(query);
    let matched = 0;
    for (const t of s.tables) {
      t.headingMatch = headingMatch;
      const tableMatches = applyTable(t);
      t.el.hidden = tableMatches === 0 && !headingMatch;
      matched += tableMatches;
    }
    const visible = headingMatch || matched > 0 || (s.tables.length === 0 && bodyText(s).includes(query));
    return { visible, matched };
  };

  const restoreSections = () => {
    for (const s of sections) {
      for (const t of s.tables) {
        t.headingMatch = false;
        t.el.hidden = false;
        applyTable(t);
      }
      s.el.hidden = false;
      if (s.toc) {
        s.toc.hidden = false;
      }
    }
    if (savedOpen) {
      sections.forEach((s, i) => {
        s.details.open = savedOpen[i];
      });
      savedOpen = null;
    }
    status.textContent = "";
    root.classList.remove("searching");
  };

  const runGlobal = () => {
    globalQuery = globalInput.value.trim().toLowerCase();
    if (!globalQuery) {
      restoreSections();
      return;
    }
    savedOpen ??= sections.map((s) => s.details.open);
    root.classList.add("searching");
    let rows = 0;
    let found = 0;
    for (const s of sections) {
      const result = sectionMatches(s, globalQuery);
      s.el.hidden = !result.visible;
      if (s.toc) {
        s.toc.hidden = !result.visible;
      }
      if (result.visible) {
        s.details.open = true;
        found += 1;
        rows += result.matched;
      }
    }
    status.textContent = found === 0 ? "No matches" : `${plural(rows, "row", "rows")} in ${plural(found, "section", "sections")}`;
  };

  const sortTable = (t, column, th) => {
    t.sortDirection = t.sortColumn === column ? -t.sortDirection : 1;
    t.sortColumn = column;
    const keyed = rowsOf(t).map((row) => [row.cells[column]?.textContent ?? "", row]);
    keyed.sort((a, b) => t.sortDirection * collator.compare(a[0], b[0]));
    t.rows = keyed.map((pair) => pair[1]);
    const fragment = doc.createDocumentFragment();
    for (const row of t.rows) {
      fragment.append(row);
    }
    t.tbody.append(fragment);
    for (const cell of th.parentElement.cells) {
      cell.removeAttribute("aria-sort");
    }
    th.setAttribute("aria-sort", t.sortDirection === 1 ? "ascending" : "descending");
    applyTable(t);
  };

  const openTarget = (id) => {
    const target = id ? doc.getElementById(id) : null;
    if (!target) {
      return;
    }
    const section = target.closest("section.sec");
    if (section?.hidden) {
      globalInput.value = "";
      runGlobal();
    }
    const details = target.closest("details") ?? target.querySelector("details");
    if (details) {
      details.open = true;
    }
    root.classList.remove("toc-open");
    doc.getElementById("toc-toggle").setAttribute("aria-expanded", "false");
  };

  doc.addEventListener("click", (event) => {
    const sortButton = event.target.closest("button.sort");
    if (sortButton) {
      const th = sortButton.closest("th");
      const t = tables.get(th.closest(".tbl"));
      if (t) {
        sortTable(t, th.cellIndex, th);
      }
      return;
    }
    const more = event.target.closest("button.more");
    if (more) {
      const t = tables.get(more.closest(".tbl"));
      t.showAll = true;
      applyTable(t);
      return;
    }
    const link = event.target.closest("a[href^='#']");
    if (link) {
      openTarget(decodeURIComponent(link.getAttribute("href").slice(1)));
      return;
    }
    event.target.closest(".bars tbody tr")?.querySelector("a[href^='#']")?.click();
  });

  const debouncedGlobal = debounce(runGlobal, 160);

  const debouncedLocal = debounce((t, value) => {
    t.local = value;
    applyTable(t);
  }, 120);

  doc.addEventListener("input", (event) => {
    const input = event.target;
    if (input === globalInput) {
      debouncedGlobal();
      return;
    }
    if (input.classList.contains("flt")) {
      const t = tables.get(input.closest(".tbl"));
      debouncedLocal(t, input.value.trim().toLowerCase());
    }
  });

  doc.addEventListener("keydown", (event) => {
    const active = doc.activeElement;
    const typing = active && (active.tagName === "INPUT" || active.tagName === "TEXTAREA" || active.isContentEditable);
    if (event.key === "/" && !typing && !event.ctrlKey && !event.metaKey && !event.altKey) {
      event.preventDefault();
      globalInput.focus();
      globalInput.select();
      return;
    }
    if (event.key === "Escape" && active === globalInput && globalInput.value) {
      globalInput.value = "";
      runGlobal();
      return;
    }
    if (event.key === "Escape" && root.classList.contains("toc-open")) {
      root.classList.remove("toc-open");
      doc.getElementById("toc-toggle").setAttribute("aria-expanded", "false");
    }
  });

  const setAll = (open) => {
    for (const s of sections) {
      if (!s.el.hidden) {
        s.details.open = open;
      }
    }
  };

  doc.getElementById("expand-all").addEventListener("click", () => setAll(true));
  doc.getElementById("collapse-all").addEventListener("click", () => setAll(false));

  const tocToggle = doc.getElementById("toc-toggle");
  tocToggle.addEventListener("click", () => {
    const open = root.classList.toggle("toc-open");
    tocToggle.setAttribute("aria-expanded", String(open));
  });

  const themeButton = doc.getElementById("theme");
  const readTheme = () => {
    try {
      return globalThis.localStorage.getItem(themeKey) ?? "auto";
    } catch {
      return "auto";
    }
  };
  const applyTheme = (theme) => {
    if (theme === "auto") {
      delete root.dataset.theme;
    } else {
      root.dataset.theme = theme;
    }
    themeButton.textContent = `Theme: ${theme}`;
  };
  applyTheme(themes.includes(readTheme()) ? readTheme() : "auto");
  themeButton.addEventListener("click", () => {
    const next = themes[(themes.indexOf(root.dataset.theme ?? "auto") + 1) % themes.length];
    applyTheme(next);
    try {
      globalThis.localStorage.setItem(themeKey, next);
    } catch {
      themeButton.dataset.persisted = "false";
    }
  });

  let printState = null;
  globalThis.addEventListener("beforeprint", () => {
    printState = sections.map((s) => s.details.open);
    for (const s of sections) {
      s.details.open = true;
    }
  });
  globalThis.addEventListener("afterprint", () => {
    if (printState) {
      sections.forEach((s, i) => {
        s.details.open = printState[i];
      });
      printState = null;
    }
  });

  const nav = doc.getElementById("toc");
  const keepVisible = (link) => {
    const top = link.offsetTop;
    if (top < nav.scrollTop || top + link.offsetHeight > nav.scrollTop + nav.clientHeight) {
      nav.scrollTop = Math.max(0, top - nav.clientHeight / 3);
    }
  };

  if ("IntersectionObserver" in globalThis) {
    let current = null;
    const observer = new IntersectionObserver((entries) => {
      for (const entry of entries) {
        if (!entry.isIntersecting) {
          continue;
        }
        const link = tocLinks.get(entry.target.id);
        if (link && link !== current) {
          current?.removeAttribute("aria-current");
          link.setAttribute("aria-current", "true");
          keepVisible(link);
          current = link;
        }
      }
    }, { rootMargin: "-15% 0px -75% 0px" });
    for (const s of sections) {
      observer.observe(s.el);
    }
    for (const sub of doc.querySelectorAll(".sub[id]")) {
      observer.observe(sub);
    }
  }

  if (globalThis.location.hash) {
    openTarget(decodeURIComponent(globalThis.location.hash.slice(1)));
  }
  globalThis.addEventListener("hashchange", () => openTarget(decodeURIComponent(globalThis.location.hash.slice(1))));
})();

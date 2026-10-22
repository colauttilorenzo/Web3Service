// Behaviour shared by every documentation page: tabs, copy buttons, table of contents, mobile menu and ⌘K search.
(() => {
  const article = document.getElementById('article');
  const toc = document.getElementById('toc');
  const store = {
    get(key) { try { return localStorage.getItem(key); } catch { return null; } },
    set(key, value) { try { localStorage.setItem(key, value); } catch { /* storage unavailable */ } },
  };

  function toast(text) {
    const el = document.getElementById('toast');
    el.textContent = text;
    el.hidden = false;
    clearTimeout(toast.timer);
    toast.timer = setTimeout(() => { el.hidden = true; }, 1600);
  }

  async function copyText(text, fallbackNode) {
    try {
      await navigator.clipboard.writeText(text);
      toast('Copiato');
    } catch {
      const range = document.createRange();
      range.selectNodeContents(fallbackNode);
      const selection = getSelection();
      selection.removeAllRanges();
      selection.addRange(range);
      toast('Selezionato: premi ⌘C');
    }
  }

  // Tabs: a choice (e.g. pnpm) applies to every tab group with the same name and is remembered.
  const tabGroups = [...document.querySelectorAll('.tabs')];
  function selectTab(group, label) {
    tabGroups.filter(tabs => tabs.dataset.group === group).forEach(tabs => tabs.selectTab(label));
  }
  tabGroups.forEach(tabs => {
    const group = tabs.dataset.group;
    const panes = [...tabs.querySelectorAll(':scope > .tab-pane')];
    const bar = document.createElement('div');
    bar.className = 'tab-bar';
    bar.setAttribute('role', 'tablist');
    tabs.selectTab = label => {
      const chosen = panes.some(pane => pane.dataset.label === label) ? label : panes[0].dataset.label;
      panes.forEach(pane => { pane.hidden = pane.dataset.label !== chosen; });
      bar.querySelectorAll('button').forEach(button => button.setAttribute('aria-selected', String(button.textContent === chosen)));
    };
    panes.forEach(pane => {
      const button = document.createElement('button');
      button.type = 'button';
      button.setAttribute('role', 'tab');
      button.textContent = pane.dataset.label;
      button.addEventListener('click', () => {
        store.set('tab:' + group, pane.dataset.label);
        selectTab(group, pane.dataset.label);
      });
      bar.appendChild(button);
    });
    tabs.prepend(bar);
    tabs.selectTab(store.get('tab:' + group));
  });

  document.querySelectorAll('.code').forEach(block => {
    const button = document.createElement('button');
    button.type = 'button';
    button.className = 'copy';
    button.textContent = 'Copia';
    const code = block.querySelector('code');
    button.addEventListener('click', () => copyText(code.textContent, code));
    block.appendChild(button);
  });
  if (window.Prism) document.querySelectorAll('pre code[class*="language-"]').forEach(el => window.Prism.highlightElement(el));

  document.getElementById('copy-page')?.addEventListener('click', () => copyText(article.innerText, article));

  // Table of contents with the current section highlighted.
  const heads = [...article.querySelectorAll('h2[id]')];
  if (heads.length) {
    toc.innerHTML = '<div class="toc-head"><svg viewBox="0 0 24 24" width="14" height="14" aria-hidden="true"><path d="M4 6h16M4 12h11M4 18h14" stroke="currentColor" stroke-width="1.8"/></svg>In questa pagina</div>'
      + heads.map(h => `<a href="#${h.id}">${h.textContent}</a>`).join('');
    const links = [...toc.querySelectorAll('a')];
    const observer = new IntersectionObserver(entries => {
      const visible = entries.filter(e => e.isIntersecting).sort((a, b) => a.boundingClientRect.top - b.boundingClientRect.top)[0];
      if (visible) links.forEach(a => a.classList.toggle('on', a.getAttribute('href') === '#' + visible.target.id));
    }, { rootMargin: '-60px 0px -70% 0px' });
    heads.forEach(h => observer.observe(h));
  }

  document.querySelectorAll('.nav-group-head').forEach(head => head.addEventListener('click', () => {
    head.setAttribute('aria-expanded', String(head.getAttribute('aria-expanded') !== 'true'));
  }));
  const menu = document.getElementById('menu-btn');
  menu.addEventListener('click', () => menu.setAttribute('aria-expanded', String(document.body.classList.toggle('nav-open'))));
  document.querySelector('.nav-link.on')?.scrollIntoView({ block: 'nearest' });

  // Search over every page; the index is generated at build time in search-index.js.
  const index = window.DOCS_SEARCH ?? [];
  const search = document.getElementById('search');
  const input = document.getElementById('search-input');
  const results = document.getElementById('search-results');
  let active = 0;
  let matches = [];
  function draw() {
    const q = input.value.trim().toLowerCase();
    matches = (q ? index.filter(e => e.title.toLowerCase().includes(q) || e.text.includes(q)) : index.filter(e => !e.href.includes('#'))).slice(0, 12);
    active = Math.min(active, Math.max(matches.length - 1, 0));
    results.innerHTML = matches.length
      ? matches.map((m, i) => `<a href="${m.href}" class="${i === active ? 'active' : ''}">${m.title}<span>${m.sub}</span></a>`).join('')
      : '<div class="search-empty">Nessun risultato. Prova con un metodo (mint), un tipo (ITransaction) o un framework.</div>';
  }
  function openSearch() { search.hidden = false; input.value = ''; active = 0; draw(); input.focus(); }
  function closeSearch() { search.hidden = true; }
  document.getElementById('search-btn').addEventListener('click', openSearch);
  input.addEventListener('input', () => { active = 0; draw(); });
  search.addEventListener('click', e => { if (e.target === search) closeSearch(); });
  document.addEventListener('keydown', e => {
    if ((e.metaKey || e.ctrlKey) && e.key.toLowerCase() === 'k') {
      e.preventDefault();
      if (search.hidden) openSearch(); else closeSearch();
    } else if (!search.hidden) {
      if (e.key === 'Escape') closeSearch();
      else if (e.key === 'ArrowDown') { active = Math.min(active + 1, matches.length - 1); draw(); e.preventDefault(); }
      else if (e.key === 'ArrowUp') { active = Math.max(active - 1, 0); draw(); e.preventDefault(); }
      else if (e.key === 'Enter' && matches[active]) location.href = matches[active].href;
    }
  });
})();

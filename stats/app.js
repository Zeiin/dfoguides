// DFO Class Statistics. Reads window.DFO_DATA (data/data.js) and builds the page.
(() => {
  'use strict';

  const D = window.DFO_DATA;
  const app = document.getElementById('app');
  const tip = document.getElementById('tooltip');
  if (!D) {
    app.textContent = 'Statistics are not available yet.';
    return;
  }

  // Base class display order; unlisted ones follow.
  const BASE_ORDER = ['Slayer (M)', 'Slayer (F)', 'Fighter (M)', 'Fighter (F)', 'Gunner (M)', 'Gunner (F)', 'Mage (M)', 'Mage (F)',
    'Priest (M)', 'Priest (F)', 'Thief', 'Knight', 'Demonic Lancer', 'Agent', 'Archer', 'Dark Knight', 'Creator'];

  const GROUPS = { all: 'All Classes', dps: 'DPS', support: 'Saders' };

  const EMBLEM_COLORS = { Red: '#D8645B', Yellow: '#E2C458', Green: '#6FBF73', Blue: '#5B9BD8', Multicolored: 'conic-gradient(#D8645B, #E2C458, #6FBF73, #5B9BD8, #D8645B)' };

  // ---------- small helpers ----------

  // Builds an element; text is added as text, never HTML.
  function h(tag, props = {}, ...kids) {
    const el = document.createElement(tag);
    for (const [key, value] of Object.entries(props)) {
      if (value == null || value === false) continue;
      if (key === 'class') el.className = value;
      else if (key === 'style') el.style.cssText = value;
      else if (key.startsWith('on')) el.addEventListener(key.slice(2), value);
      else el.setAttribute(key, value === true ? '' : value);
    }
    for (const kid of kids.flat()) {
      if (kid == null || kid === false) continue;
      el.append(kid.nodeType ? kid : document.createTextNode(kid));
    }
    return el;
  }

  const pct = (v) => `${v.toFixed(1)}%`;
  const num = (n) => n.toLocaleString('en-US');
  const prettyName = (s) => s.replace(' | ', ' \u00B7 ').replace(/:1/g, ' (damage)').replace(/:2/g, ' (cooldown)');
  const classByKey = new Map(D.classes.map((c) => [c.key, c]));
  const classByDisplay = new Map(D.classes.map((c) => [c.display, c]));

  // ---------- tooltip ----------

  function showTip(event, row, total) {
    tip.replaceChildren(
      h('div', { class: 'tv' }, pct(row.percent)),
      h('div', { class: 'tn' }, prettyName(row.name)),
      h('div', { class: 'td' }, total ? `${num(row.count)} of ${num(total)}` : `${num(row.count)}`),
    );
    tip.hidden = false;
    moveTip(event);
  }
  function moveTip(event) {
    const x = event.clientX ?? event.target.getBoundingClientRect().left;
    const y = event.clientY ?? event.target.getBoundingClientRect().bottom;
    const w = tip.offsetWidth, hgt = tip.offsetHeight;
    tip.style.left = `${Math.min(x + 14, window.innerWidth - w - 8)}px`;
    tip.style.top = `${y + hgt + 20 > window.innerHeight ? y - hgt - 12 : y + 16}px`;
  }
  const hideTip = () => { tip.hidden = true; };

  // ---------- icons ----------

  // Item icon for a row; names without an icon get a blank spacer so labels line up.
  // Weapons also get a frame in their rarity colour (D.weaponRarity: weapon name -> rarity).
  const itemIcon = (category) => (row) => {
    const file = (D.icons[category] || {})[row.name];
    const rarity = category === 'weapons' ? (D.weaponRarity || {})[row.name] : null;
    const cls = `item-icon${rarity ? ` rarity-${rarity.toLowerCase()}` : ''}`;
    // Every level of a Platinum title has the same icon, so the level goes in the corner.
    const level = category === 'titles' ? row.name.match(/ \[(\d+)\]$/) : null;
    if (level) return h('span', { class: 'item-icon level-icon' }, file ? h('img', { src: `img/items/${file}`, alt: '', loading: 'lazy' }) : null, h('span', { class: 'level-badge' }, level[1]));
    return file ?h('img', { class: cls, src: `img/items/${file}`, alt: '', loading: 'lazy' }) : h('span', { class: cls });
  };

  const classIcon = (row) => {
    const c = classByDisplay.get(row.name);
    return c && c.portrait ? h('img', { class: 'icon', src: `img/classes/${c.portrait}`, alt: '', loading: 'lazy' }) : null;
  };

  // Emblem icon, or a color dot when there is none.
  const emblemIcon = (color) => (row) => (D.icons.emblems || {})[row.name]
    ? itemIcon('emblems')(row)
    : h('i', { class: 'dot', style: `background:${EMBLEM_COLORS[color]}` });

  // ---------- components ----------

  // One ranked list of bars; `initial` rows show, the rest behind "Show all".
  // counts: print the character count next to the percent.
  // absolute: scale bars against 100% instead of the top row.
  function barCard({ title, rows, initial = 8, icon, titleIcon, wide = false, longLabels = false, counts = false, absolute = false, labelOf }) {
    rows = (rows || []).filter((r) => r.name !== '(none)'); // nothing worn (e.g. a rental weapon): not worth a row
    const card = h('section', { class: `card${wide ? ' wide' : ''}${longLabels ? ' long-labels' : ''}${counts ? ' with-counts' : ''}` }, h('h3', {}, titleIcon, title));
    if (!rows || rows.length === 0) {
      card.append(h('div', { class: 'empty' }, 'No data.'));
      return card;
    }

    const max = absolute ? 100 : Math.max(...rows.map((r) => r.percent), 0.1);
    const list = h('ul', { class: 'bar-list' });
    const table = h('table', { class: 'bar-table' }, h('thead', {}, h('tr', {}, h('th', {}, 'Name'), h('th', { class: 'num' }, 'Count'), h('th', { class: 'num' }, '%'))));
    const body = h('tbody');
    table.append(body);

    rows.forEach((row, i) => {
      const extra = i >= initial ? ' extra' : '';
      // Total behind this percent, for the tooltip.
      const total = row.percent > 0 ? Math.round(row.count / (row.percent / 100)) : 0;
      const label = h('div', { class: 'label' }, icon ? icon(row) : null, h('span', {}, prettyName(labelOf ? labelOf(row) : row.name)));
      const item = h('li', { class: `bar-row${extra}`, tabindex: '0' },
        label,
        h('div', { class: 'track' }, h('i', { class: 'fill', style: `width:${Math.max((row.percent / max) * 100, 0.8)}%` })),
        h('div', { class: 'value' }, counts ? `${num(row.count)} \u00B7 ${pct(row.percent)}` : pct(row.percent)),
      );
      item.addEventListener('pointermove', (e) => showTip(e, row, total));
      item.addEventListener('pointerleave', hideTip);
      item.addEventListener('focus', (e) => showTip(e, row, total));
      item.addEventListener('blur', hideTip);
      list.append(item);
      body.append(h('tr', { class: extra.trim() }, h('td', {}, prettyName(row.name)), h('td', { class: 'num' }, num(row.count)), h('td', { class: 'num' }, pct(row.percent))));
    });
    card.append(list, table);

    if (rows.length > initial) {
      const button = h('button', { class: 'more', type: 'button' }, `Show all ${rows.length}`);
      button.addEventListener('click', () => {
        const open = card.classList.toggle('expanded');
        button.textContent = open ? 'Show fewer' : `Show all ${rows.length}`;
      });
      card.append(button);
    }
    return card;
  }

  function tabs(basePath, list, active, query = '') {
    return h('nav', { class: 'tabs', role: 'tablist' }, list.map((t) =>
      h('a', { class: `tab${t.id === active ? ' active' : ''}`, href: `#${basePath}/${t.id}${query}`, role: 'tab', 'aria-selected': String(t.id === active) }, t.label)));
  }

  // ---------- filters (class pages) ----------

  const F = window.DFO_FILTER;
  const familyOf = (setName) => D.setFamilies[setName] || setName;

  // The filter lives in the URL so a view can be shared: #/class/<key>/<tab>?set=A~B&oath=C&min=100000&max=125000
  function parseFilter(query) {
    const q = new URLSearchParams(query);
    const list = (name) => new Set((q.get(name) || '').split('~').filter(Boolean));
    const limit = (name) => (q.get(name) ? Number(q.get(name)) : null);
    return { sets: list('set'), oaths: list('oath'), min: limit('min'), max: limit('max') };
  }

  const filterActive = (f) => f.sets.size > 0 || f.oaths.size > 0 || f.min != null || f.max != null;

  function filterQuery(f) {
    const q = new URLSearchParams();
    if (f.sets.size) q.set('set', [...f.sets].join('~'));
    if (f.oaths.size) q.set('oath', [...f.oaths].join('~'));
    if (f.min != null) q.set('min', String(f.min));
    if (f.max != null) q.set('max', String(f.max));
    const text = q.toString();
    return text ? `?${text}` : '';
  }

  // A class's character rows load once, when a filter is first used.
  const recordCache = new Map();
  function loadRecords(key) {
    if (recordCache.has(key)) return Promise.resolve(recordCache.get(key));
    return new Promise((resolve, reject) => {
      const script = document.createElement('script');
      script.src = `data/records/${encodeURIComponent(key)}.js`;
      script.onload = () => { recordCache.set(key, window.DFO_RECORDS[key]); resolve(window.DFO_RECORDS[key]); };
      script.onerror = () => reject(new Error('Could not load the characters.'));
      document.head.append(script);
    });
  }

  // Several classes at once, a few files at a time so the browser is not flooded.
  async function loadManyRecords(keys, batch = 6) {
    const out = [];
    for (let i = 0; i < keys.length; i += batch) out.push(...await Promise.all(keys.slice(i, i + batch).map(loadRecords)));
    return out;
  }

  const FAMILY_ORDER = ['Cleansing Darkness', 'Dragon Arena Uprising', "Hideout's Endless Gold", 'Overwhelming Nature', 'Death in the Shadows', 'Magic Domain',
    'Serendipity', 'Ethereal Orb Arts', 'Ancient Battlefield Valkyrie', 'Soul Fairy', 'Beyond Limit Energy', 'Alpha of the Pack Hunt'];

  // Set and oath buttons (emblems only) and the fame range. Choosing nothing in a row means every character.
  function filterBar(path, tab, info, base, filter) {
    const go = (next) => { location.hash = `#${path}/${tab}${filterQuery(next)}`; };
    const toggled = (set, value) => { const next = new Set(set); if (next.has(value)) next.delete(value); else next.add(value); return next; };
    // Icon-only button; the name is the tooltip.
    const chip = (label, iconFile, active, onClick) => h('button', { class: `fchip icon-only${active ? ' active' : ''}`, type: 'button', title: label, 'aria-label': label, 'aria-pressed': String(active), onclick: onClick },
      h('img', { class: 'item-icon', src: `img/items/${iconFile}`, alt: '' }));

    // Every emblem set and its oath set, always in the same order so the two rows line up (an oath uses the emblem of its themed set).
    const setChips = FAMILY_ORDER.filter((family) => D.familyIcons[family])
      .map((family) => chip(family, D.familyIcons[family], filter.sets.has(family), () => go({ ...filter, sets: toggled(filter.sets, family) })));
    const familyOfIcon = new Map(Object.entries(D.familyIcons).map(([family, file]) => [file, family]));
    const oathRank = (name) => FAMILY_ORDER.indexOf(familyOfIcon.get(D.icons.oath[name]));
    const oathChips = Object.keys(D.icons.oath || {}).sort((a, b) => oathRank(a) - oathRank(b))
      .map((name) => chip(name.replace(/ Oath$/, ''), D.icons.oath[name], filter.oaths.has(name), () => go({ ...filter, oaths: toggled(filter.oaths, name) })));

    const fameInput = (field, label, placeholder) => {
      const input = h('input', { class: 'fame-input', type: 'number', inputmode: 'numeric', min: '0', step: '100', placeholder: num(placeholder), 'aria-label': label, value: filter[field] ?? '' });
      input.addEventListener('change', () => go({ ...filter, [field]: input.value === '' ? null : Number(input.value) }));
      return input;
    };
    const clear = filterActive(filter)
      ? h('button', { class: 'fchip clear', type: 'button', onclick: () => go({ sets: new Set(), oaths: new Set(), min: null, max: null }) }, 'Clear filters')
      : null;

    return h('div', { class: 'filters' },
      h('div', { class: 'filter-row' }, h('span', { class: 'filter-label' }, 'Set'), setChips),
      h('div', { class: 'filter-row' }, h('span', { class: 'filter-label' }, 'Oath'), oathChips),
      h('div', { class: 'filter-row' }, h('span', { class: 'filter-label' }, 'Fame'),
        fameInput('min', 'Lowest fame', info.fameMin), h('span', { class: 'fame-dash' }, '–'), fameInput('max', 'Highest fame', info.fameMax), clear));
  }

  // A filter that keeps very few of the characters says little about the class: warn below this share.
  const LOW_SHARE = 0.05;
  // Always returns a node (replaceChildren would print "null"): an empty fragment when there is nothing to warn about.
  function lowSampleNote(filtered, shown, all) {
    if (!filtered || shown === 0 || shown / all >= LOW_SHARE) return document.createDocumentFragment();
    return h('div', { class: 'warning', role: 'status' },
      `Low representation: only ${num(shown)} of ${num(all)} characters (${pct((100 * shown) / all)}) match these filters, so the statistics may not be representative.`);
  }

  const sectionTitle = (text) => h('h2', { class: 'section-title' }, text);
  const grid = (...cards) => h('div', { class: 'card-grid' }, cards);

  // Black Fang (3/3): the share wearing all three, then the same share split by gear set (percent of everyone counted, so the rows add up to the first one).
  const blackFangCard = (s) => {
    const all = s.blackFangCounts.find((r) => r.name === '3 of 3') || { count: 0, percent: 0 };
    return barCard({
      title: 'Black Fang (3/3)',
      rows: [{ name: 'All sets', count: all.count, percent: all.percent }, ...s.blackFangSets],
      icon: itemIcon('sets'),
      counts: true,
      initial: 9,
    });
  };

  // The most used oath sets, one card each with the rates of its options within that set.
  const oathBoxes = (s) => h('div', { class: 'card-grid three' },s.oathBreakdown.map((set) => {
    const file = (D.icons.oath || {})[set.set];
    return barCard({
      title: `${set.set.replace(/ Oath$/, '')} · ${pct(set.percent)}`,
      titleIcon: file ? h('img', { class: 'item-icon', src: `img/items/${file}`, alt: '' }) : null,
      rows: set.options,
      labelOf: (row) => row.name.replace(/^[^:]+: /, ''),
      absolute: true,
    });
  }));

  const oathContent = (s) => h('div', {}, grid(barCard({ title: 'Oath sets', rows: s.oathSets, icon: itemIcon('oath') })), oathBoxes(s));

  const distinctCard = (s) => barCard({ title: 'Distinct pieces', rows: s.distinctItems, icon: itemIcon('distinct'), initial: 10, longLabels: true });
  const exaltedCard = (s) => barCard({ title: 'Exalted pieces', rows: s.exalted, icon: itemIcon('exalted'), counts: true, absolute: true });

  // ---------- pages ----------

  function renderHome() {
    document.title = 'DFO Class Statistics';
    const stats = D.stats;
    const groupTile = (id) => h('a', { class: 'group-tile', href: `#/group/${id}` },
      h('div', { class: 'name' }, GROUPS[id]),
      h('div', { class: 'count' }, num(stats[id].sample), h('small', {}, 'characters')));

    // Classes grouped by base class.
    const bases = new Map();
    for (const c of D.classes) {
      if (!bases.has(c.baseClass)) bases.set(c.baseClass, []);
      bases.get(c.baseClass).push(c);
    }
    const ordered = [...bases.keys()].sort((a, b) => {
      const ia = BASE_ORDER.indexOf(a), ib = BASE_ORDER.indexOf(b);
      return (ia < 0 ? 99 : ia) - (ib < 0 ? 99 : ib);
    });

    const maxShare = Math.max(...D.classes.map((c) => c.share));
    const sections = ordered.map((base) => {
      const cards = bases.get(base).sort((a, b) => b.sample - a.sample).map((c) =>
        h('a', { class: 'class-card', href: `#/class/${c.key}`, 'data-name': `${c.display} ${base}`.toLowerCase() },
          h('div', { class: 'art' }, c.portrait ? h('img', { src: `img/classes/${c.portrait}`, alt: `${c.display} portrait`, loading: 'lazy' }) : null),
          h('div', { class: 'meta' },
            h('div', { class: 'cname' }, c.display),
            h('div', { class: 'csub' }, `${num(c.sample)} characters \u00B7 ${pct(c.share)}`),
            h('div', { class: 'mini-bar', title: 'Share of all characters' }, h('i', { style: `width:${(c.share / maxShare) * 100}%` })))));
      const members = bases.get(base);
      const total = members.reduce((sum, c) => sum + c.sample, 0);
      const title = h('h2', { class: 'section-title' }, base, h('span', { class: 'section-meta' }, `${total} Characters \u2022 ${pct((100 * total) / D.totalCharacters)}`));
      return h('section', { class: 'base-group' }, title, h('div', { class: 'class-grid' }, cards));
    });

    const search = h('input', { class: 'search', type: 'search', placeholder: 'Find a class\u2026', 'aria-label': 'Find a class' });
    search.addEventListener('input', () => {
      const q = search.value.trim().toLowerCase();
      document.querySelectorAll('.class-card').forEach((card) => { card.hidden = q !== '' && !card.dataset.name.includes(q); });
      document.querySelectorAll('.base-group').forEach((g) => { g.hidden = !g.querySelector('.class-card:not([hidden])'); });
    });

    app.replaceChildren(
      h('h1', {}, 'Class Statistics'),
      h('div', { class: 'group-tiles' }, groupTile('all'), groupTile('dps'), groupTile('support')),
      h('div', { class: 'search-row' }, search),
      ...sections);
  }

  async function renderGroup(id, tab = 'rates', query = '') {
    const title = GROUPS[id], base = D.stats[id];
    if (!title || !base) return renderNotFound();
    document.title = `${title} \u00B7 DFO Class Statistics`;

    // Without a filter the exported numbers are used; with one every member class's characters are loaded and recounted.
    const members = D.classes.filter((c) => id === 'all' || (id === 'support') === (c.role === 'Support'));
    const range = { fameMin: Math.min(...members.map((c) => c.fameMin)), fameMax: Math.max(...members.map((c) => c.fameMax)) };
    const filter = parseFilter(query);
    const filtered = filterActive(filter);
    let s = base;
    if (filtered) {
      const here = location.hash;
      if (!members.every((c) => recordCache.has(c.key))) app.replaceChildren(h('div', { class: 'empty' }, 'Loading\u2026'));
      let loaded;
      try { loaded = await loadManyRecords(members.map((c) => c.key)); } catch (e) { app.replaceChildren(h('div', { class: 'empty' }, e.message)); return; }
      if (location.hash !== here) return; // moved on while loading
      s = F.computeGroupStats(members.map((c, i) => ({ display: c.display, table: loaded[i].s, rows: F.filterRows(loaded[i].s, loaded[i].r, familyOf, filter) })));
    }

    const tabList = [{ id: 'rates', label: 'Play rates' }, { id: 'gear', label: 'Gear' }, { id: 'oath', label: 'Oath' }, { id: 'special', label: 'Special pieces' }];
    const active = tabList.some((t) => t.id === tab) ? tab : 'rates';

    let content;
    if (s.sample === 0) {
      content = h('div', { class: 'empty' }, 'No characters match these filters.');
    } else if (active === 'rates') {
      content = grid(barCard({ title: 'Play rate by class', rows: s.classShare, initial: 15, icon: classIcon, wide: true }));
    } else if (active === 'gear') {
      content = grid(barCard({ title: 'Gear sets', rows: s.sets, icon: itemIcon('sets') }), barCard({ title: 'Titles', rows: s.titles, icon: itemIcon('titles'), longLabels: true }));
    } else if (active === 'oath') {
      content = oathContent(s);
    } else {
      content = grid(distinctCard(s), exaltedCard(s), blackFangCard(s));
    }

    app.replaceChildren(
      h('div', { class: 'crumbs' }, h('a', { href: '#/' }, 'All classes'), ' / ', title),
      h('div', { class: 'page-head' },
        h('div', { class: 'titles' }, h('h1', {}, title)),
        h('div', { class: 'hero-figure' }, h('div', { class: 'value' }, num(s.sample)), h('div', { class: 'label' }, filtered ? `of ${num(base.sample)} characters` : 'characters'))),
      tabs(`/group/${id}`, tabList, active, filterQuery(filter)),
      filterBar(`/group/${id}`, active, range, base, filter),
      lowSampleNote(filtered, s.sample, base.sample),
      content);
  }

  async function renderClass(key, tab = 'gear', query = '') {
    const info = classByKey.get(key), base = D.stats[key];
    if (!info || !base) return renderNotFound();
    document.title = `${info.display} \u00B7 DFO Class Statistics`;

    // Without a filter the exported numbers are used; with one they are recomputed from the class's characters.
    const filter = parseFilter(query);
    const filtered = filterActive(filter);
    let s = base;
    if (filtered) {
      const here = location.hash;
      if (!recordCache.has(key)) app.replaceChildren(h('div', { class: 'empty' }, 'Loading\u2026'));
      let records;
      try { records = await loadRecords(key); } catch (e) { app.replaceChildren(h('div', { class: 'empty' }, e.message)); return; }
      if (location.hash !== here) return; // moved on while loading
      s = F.computeClassStats(records.s, F.filterRows(records.s, records.r, familyOf, filter));
    }

    const tabList = [{ id: 'gear', label: 'Gear' }, { id: 'oath', label: 'Oath' }, { id: 'skills', label: 'Skills' }, { id: 'avatar', label: 'Avatar & Emblems' }];
    const active = tabList.some((t) => t.id === tab) ? tab : 'gear';

    let content;
    if (s.sample === 0) {
      content = h('div', { class: 'empty' }, 'No characters match these filters.');
    } else if (active === 'gear') {
      content = h('div', {},
        grid(barCard({ title: 'Gear sets', rows: s.sets, icon: itemIcon('sets') }), barCard({ title: 'Weapons', rows: s.weapons, icon: itemIcon('weapons') }), barCard({ title: 'Titles', rows: s.titles, icon: itemIcon('titles'), longLabels: true })),
        sectionTitle('Special pieces'),
        grid(distinctCard(s), exaltedCard(s), blackFangCard(s)));
    } else if (active === 'oath') {
      content = oathContent(s);
    } else if (active === 'skills') {
      const tree = D.skillTree[key] || [];
      const byName = new Map(tree.map((sk) => [sk.name, sk]));
      content = h('div', {},
        grid(
          setupCard('VP', s.vpSets, byName, parseVpPart, (part) => `Evolve${part.option}`),
          setupCard('Enhancement', s.enhancementSets, byName, parseEnhancementPart, (part) => `Enhance${part.option}`)),
        grid(
          optionTable('VP by skill', s.vpSkills, byName, /^(.*) \(VP([12])\)$/, ['VP1', 'VP2'], ['Evolve1', 'Evolve2']),
          optionTable('Enhancement by skill', s.enhancementPicks, byName, /^(.*) \((damage|cooldown)\)$/, ['Damage', 'Cooldown'], ['Enhance1', 'Enhance2'])),
        skillTreeCard(tree, s.skills));
    } else {
      const byColor = ['Red', 'Yellow', 'Green', 'Blue', 'Multicolored'].map((color) => ({
        color,
        rows: s.emblemEffects.filter((r) => r.name.startsWith(`${color}: `)),
      })).filter((c) => c.rows.length > 0);
      content = h('div', {},
        grid(
          barCard({ title: 'Creature', rows: s.creatures, icon: itemIcon('creatures') }),
          barCard({ title: 'Weapon avatar', rows: s.weaponAvatarLevels, icon: itemIcon('weaponAvatars') })),
        sectionTitle('Platinum emblem'),
        h('div', { class: 'card-grid tight' },
          barCard({ title: 'Top avatar', rows: s.platinumTop, icon: itemIcon('platinum'), initial: 6 }),
          barCard({ title: 'Bottom avatar', rows: s.platinumBottom, icon: itemIcon('platinum'), initial: 6 }),
          barCard({ title: 'Aura avatar', rows: s.platinumAura, icon: itemIcon('platinum'), initial: 6 })),
        sectionTitle('Emblem effects'),
        h('div', { class: 'card-grid tight' }, byColor.map((c) => barCard({
          title: `${c.color} sockets`,
          rows: c.rows,
          initial: 5,
          icon: emblemIcon(c.color),
          labelOf: (row) => row.name.split(': ').slice(1).join(': '),
        }))));
    }

    app.replaceChildren(
      h('div', { class: 'crumbs' }, h('a', { href: '#/' }, 'All classes'), ' / ', info.baseClass, ' / ', info.display),
      h('div', { class: 'page-head' },
        h('div', { class: 'portrait' }, info.portrait ? h('img', { src: `img/classes/${info.portrait}`, alt: `${info.display} portrait` }) : null),
        h('div', { class: 'titles' },
          h('h1', {}, info.display),
          h('div', { class: 'chips' }, h('span', { class: 'chip' }, info.baseClass), h('span', { class: 'chip gold' }, info.role === 'Support' ? 'Sader' : 'DPS'))),
        h('div', { class: 'hero-figure' }, h('div', { class: 'value' }, num(s.sample)),
          h('div', { class: 'label' }, filtered ? `of ${num(base.sample)} characters` : `characters \u00B7 ${pct(info.share)} of all`))),
      tabs(`/class/${key}`, tabList, active, filterQuery(filter)),
      filterBar(`/class/${key}`, active, info, base, filter),
      lowSampleNote(filtered, s.sample, base.sample),
      content);
  }

  // ---------- skills tab ----------

  // The game's own VP and enhancement badges; the VP ones (I / II) get extra contrast in the stylesheet.
  function badgeEl(name, extraClass = '') {
    const kind = name.startsWith('Evolve') ? ' vp-badge' : '';
    return h('img', { class: `${extraClass}${kind}`.trim(), src: `img/skills/badges/${name}.png`, alt: '' });
  }

  // Skill icon with an optional corner badge; text tile when there is no icon.
  function skillIcon(skill, badge, label) {
    const wrap = h('span', { class: 'skill-icon', title: label || (skill ? skill.name : '') });
    if (skill && skill.icon) wrap.append(h('img', { src: `img/skills/${skill.icon}`, alt: skill.name, loading: 'lazy' }));
    else wrap.append(h('span', { class: 'skill-fallback' }, ((skill && skill.name) || '?').slice(0, 3)));
    if (badge) wrap.append(badgeEl(badge, 'skill-badge'));
    return wrap;
  }

  // "Sorting VP2" -> { skill: 'Sorting', option: 2 };  "A.I.R.:1" -> { skill: 'A.I.R.', option: 1 }
  const parseVpPart = (text) => { const m = text.match(/^(.*) VP([12])$/); return m ? { skill: m[1], option: Number(m[2]) } : { skill: text, option: 0 }; };
  const parseEnhancementPart = (text) => { const m = text.match(/^(.*):([12])$/); return m ? { skill: m[1], option: Number(m[2]) } : { skill: text, option: 0 }; };

  // Full setups as rows of icons with their usage share.
  function setupCard(title, rows, byName, parsePart, badgeOf, initial = 8) {
    const card = h('section', { class: 'card' }, h('h3', {}, title));
    const list = h('ul', { class: 'setup-list' });
    rows.forEach((row, i) => {
      const icons = row.name.split(' + ').map((part) => {
        const p = parsePart(part);
        return skillIcon(byName.get(p.skill), p.option ? badgeOf(p) : null, part);
      });
      const item = h('li', { class: `setup-row${i >= initial ? ' extra' : ''}`, tabindex: '0' }, h('div', { class: 'setup-icons' }, icons), h('div', { class: 'value' }, pct(row.percent)));
      item.addEventListener('pointermove', (e) => showTip(e, row, row.percent > 0 ? Math.round(row.count / (row.percent / 100)) : 0));
      item.addEventListener('pointerleave', hideTip);
      list.append(item);
    });
    card.append(list);
    if (rows.length > initial) {
      const button = h('button', { class: 'more', type: 'button' }, `Show all ${rows.length}`);
      button.addEventListener('click', () => { const open = card.classList.toggle('expanded'); button.textContent = open ? 'Show fewer' : `Show all ${rows.length}`; });
      card.append(button);
    }
    return card;
  }

  // One row per skill with the share of each option.
  function optionTable(title, rows, byName, pattern, headings, badges) {
    const skills = new Map();
    for (const row of rows) {
      const m = row.name.match(pattern);
      if (!m) continue;
      const option = m[2] === 'VP2' || m[2] === 'cooldown' || m[2] === '2' ? 1 : 0;
      const entry = skills.get(m[1]) || { name: m[1], values: [0, 0] };
      entry.values[option] = row.percent;
      skills.set(m[1], entry);
    }
    const ordered = [...skills.values()].sort((a, b) => (b.values[0] + b.values[1]) - (a.values[0] + a.values[1]));

    const head = h('tr', {}, h('th', {}, 'Skill'), headings.map((text, i) => h('th', { class: 'num' }, h('span', { class: 'opt-head' }, badgeEl(badges[i]), text))));
    const body = h('tbody', {}, ordered.map((e) => {
      const lead = e.values[0] >= e.values[1] ? 0 : 1;
      return h('tr', {},
        h('td', {}, h('div', { class: 'skill-name' }, skillIcon(byName.get(e.name)), h('div', {}, h('div', {}, e.name), h('div', { class: 'sub' }, pct(e.values[0] + e.values[1]))))),
        e.values.map((v, i) => h('td', { class: `num${i === lead && v >= 50 ? ' lead' : ''}` }, pct(v))));
    }));
    return h('section', { class: 'card' }, h('h3', {}, title), h('table', { class: 'option-table' }, h('thead', {}, head), body));
  }

  // Rows and columns exactly as the game's skill window draws them (each skill's x and y come from the export).
  // A skill without a position goes into a last row, in required-level order. Returns { rows: [{ level, cells: [{ skill, col }] }], columns }.
  function gameLayout(skills) {
    const byRow = new Map();
    const loose = skills.filter((s) => s.x == null).sort((a, b) => a.level - b.level);
    for (const s of skills.filter((sk) => sk.x != null)) {
      if (!byRow.has(s.y)) byRow.set(s.y, []);
      byRow.get(s.y).push(s);
    }
    const rows = [...byRow.keys()].sort((a, b) => a - b).map((y) => {
      const inRow = byRow.get(y);
      return { level: Math.min(...inRow.map((s) => s.level)), cells: inRow.map((s) => ({ skill: s, col: s.x - 1 })) };
    });
    if (loose.length) rows.push({ level: '–', cells: loose.map((s, i) => ({ skill: s, col: i })) });
    const columns = Math.max(0, ...rows.flatMap((r) => r.cells.map((c) => c.col + 1)));
    return { rows, columns };
  }

  const layoutTree = (skills) => (skills.some((s) => s.x != null) ? gameLayout(skills) : inferLayout(skills));

  // Fallback for a class without game positions: a row per required level, skills in the game's list order, and a skill that needs
  // another skill in the same column below it (when that column is free). Same return shape as gameLayout.
  function inferLayout(skills) {
    const colOf = new Map();
    const lastLevel = new Map(); // skill id -> level of the deepest skill that needs it
    for (const s of skills) for (const id of s.pre || []) lastLevel.set(id, Math.max(lastLevel.get(id) || 0, s.level));
    const reserved = new Map(); // column -> level its chain runs down to; nothing else may sit in it until then
    let columns = 0;

    const rows = [...new Set(skills.map((s) => s.level))].sort((a, b) => a - b).map((level) => {
      const inRow = skills.filter((s) => s.level === level);
      const used = new Set();
      const chosen = new Map();
      // 1. A skill that needs another goes under it (or in the nearest free column when that one is taken).
      for (const s of inRow) {
        const want = (s.pre || []).map((id) => colOf.get(id)).find((c) => c !== undefined);
        if (want === undefined) continue;
        let col = want;
        for (let d = 1; used.has(col) || col < 0 || (col !== want && reserved.has(col)); d++) col = d % 2 ? want + Math.ceil(d / 2) : want - d / 2;
        chosen.set(s.id, col);
        used.add(col);
      }
      // 2. Everything else takes the leftmost column that no chain is running through.
      let next = 0;
      const cells = inRow.map((s) => {
        let col = chosen.get(s.id);
        if (col === undefined) {
          while (used.has(next) || reserved.has(next)) next++;
          col = next;
          used.add(col);
        }
        colOf.set(s.id, col);
        columns = Math.max(columns, col + 1);
        return { skill: s, col };
      });
      for (const [col, until] of [...reserved]) if (until <= level) reserved.delete(col);
      for (const { skill, col } of cells) if ((lastLevel.get(skill.id) || 0) > level) reserved.set(col, lastLevel.get(skill.id));
      return { level, cells };
    });
    return { rows, columns };
  }

  // Lines from each skill down to the skills that need it, drawn over the grid once it has a size.
  function drawTreeLinks(tree, svg) {
    const origin = tree.getBoundingClientRect();
    const spot = new Map();
    tree.querySelectorAll('.tree-cell').forEach((el) => {
      const icon = el.querySelector('.skill-icon').getBoundingClientRect();
      const cell = el.getBoundingClientRect();
      // A line leaves from under the cell's numbers and arrives at the top of the icon.
      spot.set(el.dataset.id, { x: icon.left - origin.left + icon.width / 2 + tree.scrollLeft, top: icon.top - origin.top, bottom: cell.bottom - origin.top, row: Number(el.dataset.row) });
    });
    svg.setAttribute('width', String(tree.scrollWidth));
    svg.setAttribute('height', String(tree.scrollHeight));
    svg.replaceChildren();
    tree.querySelectorAll('.tree-cell[data-pre]').forEach((el) => {
      const to = spot.get(el.dataset.id);
      for (const id of el.dataset.pre.split(',')) {
        const from = spot.get(id);
        // Only skills in neighbouring rows are joined; a longer line would cross other skills and the game's own routing is unknown.
        if (!from || !to || to.row - from.row !== 1) continue;
        const turn = (from.bottom + to.top) / 2;
        const path = document.createElementNS('http://www.w3.org/2000/svg', 'path');
        path.setAttribute('d', `M${from.x} ${from.bottom} V${turn} H${to.x} V${to.top}`);
        svg.append(path);
      }
    });
  }

  // Skill tree: the game's layout, each skill with adoption rate and average level.
  function skillTreeCard(tree, ratesList) {
    const rates = new Map(ratesList.map((r) => [r.skill, r]));
    const shown = tree.filter((sk) => sk.icon || (rates.get(sk.name) && rates.get(sk.name).takePercent > 0));
    const layout = layoutTree(shown);

    const rows = layout.rows.map(({ level, cells }, rowIndex) => h('div', { class: 'tree-row' },
      h('div', { class: 'tree-level' }, String(level)),
      h('div', { class: 'tree-cells', style: `grid-template-columns: repeat(${layout.columns}, 56px)` }, cells.map(({ skill: sk, col }) => {
        const r = rates.get(sk.name);
        const take = r ? r.takePercent : 0;
        const tone = take >= 99.5 ? 'full' : take >= 50 ? 'mid' : take > 0 ? 'low' : 'none';
        const pre = (sk.pre || []).filter((id) => shown.some((s) => s.id === id));
        return h('div', { class: `tree-cell ${tone}`, title: sk.name, style: `grid-column: ${col + 1}; grid-row: 1`, 'data-id': sk.id, 'data-row': rowIndex, 'data-pre': pre.length ? pre.join(',') : null },
          skillIcon(sk),
          h('div', { class: 'take' }, take.toFixed(1)),
          h('div', { class: 'avg' }, r ? r.averageLevelTakers.toFixed(1) : '0'));
      }))));

    const svg = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
    svg.setAttribute('class', 'tree-links');
    const box = h('div', { class: 'tree' }, svg, rows);
    // Redraws whenever the grid gets a size or changes (first paint, window resize).
    new ResizeObserver(() => drawTreeLinks(box, svg)).observe(box);
    return h('section', { class: 'card wide' }, h('h3', {}, 'Skill tree'), box);
  }
  function renderNotFound() {
    app.replaceChildren(h('h1', {}, 'Not found'), h('a', { class: 'chip gold', href: '#/' }, 'Back to all classes'));
  }

  // ---------- routing ----------

  // The part after "?" is the filter; changing only that keeps the scroll position.
  let lastPath = null;

  function route() {
    hideTip();
    const [path, query = ''] = location.hash.replace(/^#\/?/, '').split('?');
    const parts = (path || '').split('/').filter(Boolean);
    if (parts.length === 0) renderHome();
    else if (parts[0] === 'group') renderGroup(parts[1], parts[2], query);
    else if (parts[0] === 'class') renderClass(parts[1], parts[2], query);
    else renderNotFound();
    if (path !== lastPath) window.scrollTo(0, 0);
    lastPath = path;
  }

  const tableToggle = document.getElementById('table-toggle');
  const setTableView = (on) => {
    document.body.classList.toggle('table-view', on);
    tableToggle.setAttribute('aria-pressed', String(on));
  };
  tableToggle.addEventListener('click', () => setTableView(!document.body.classList.contains('table-view')));
  // ?view=table opens with every chart as a table.
  if (new URLSearchParams(location.search).get('view') === 'table') setTableView(true);

  document.getElementById('footer-note').textContent = `Snapshot ${D.generated} \u00B7 characters ${num(D.cutoffFame)}+ fame`;

  window.addEventListener('hashchange', route);
  route();
})();


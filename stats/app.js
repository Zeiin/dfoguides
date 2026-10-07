// DFO Class Statistics. Reads window.DFO_DATA (data/data.js) and builds the page.
(() => {
  'use strict';

  const D = window.DFO_DATA;
  const app = document.getElementById('app');
  const tip = document.getElementById('tooltip');

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
  const itemIcon = (category) => (row) => {
    const file = (D.icons[category] || {})[row.name];
    return file ? h('img', { class: 'item-icon', src: `img/items/${file}`, alt: '', loading: 'lazy' }) : h('span', { class: 'item-icon' });
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
  function barCard({ title, rows, initial = 8, icon, wide = false, longLabels = false, counts = false, absolute = false, labelOf }) {
    const card = h('section', { class: `card${wide ? ' wide' : ''}${longLabels ? ' long-labels' : ''}${counts ? ' with-counts' : ''}` }, h('h3', {}, title));
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

  function tabs(basePath, list, active) {
    return h('nav', { class: 'tabs', role: 'tablist' }, list.map((t) =>
      h('a', { class: `tab${t.id === active ? ' active' : ''}`, href: `#${basePath}/${t.id}`, role: 'tab', 'aria-selected': String(t.id === active) }, t.label)));
  }

  const sectionTitle = (text) => h('h2', { class: 'section-title' }, text);
  const grid = (...cards) => h('div', { class: 'card-grid' }, cards);

  // Black Fang: only the 3-of-3 share.
  const blackFangCard = (s) => barCard({
    title: 'Black Fang',
    rows: s.blackFangCounts.filter((r) => r.name === '3 of 3'),
    labelOf: () => '3 of 3',
    absolute: true,
  });

  const distinctCard = (s) => barCard({ title: 'Distinct pieces by slot', rows: s.distinctBySlot, initial: 11 });
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
      return h('section', { class: 'base-group' }, sectionTitle(base), h('div', { class: 'class-grid' }, cards));
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

  function renderGroup(id, tab = 'rates') {
    const title = GROUPS[id], s = D.stats[id];
    if (!title || !s) return renderNotFound();
    document.title = `${title} \u00B7 DFO Class Statistics`;

    const tabList = [{ id: 'rates', label: 'Play rates' }, { id: 'gear', label: 'Gear' }, { id: 'oath', label: 'Oath' }, { id: 'special', label: 'Special pieces' }];
    const active = tabList.some((t) => t.id === tab) ? tab : 'rates';

    let content;
    if (active === 'rates') {
      content = grid(barCard({ title: 'Play rate by class', rows: s.classShare, initial: 15, icon: classIcon, wide: true }));
    } else if (active === 'gear') {
      content = grid(barCard({ title: 'Gear sets', rows: s.sets, icon: itemIcon('sets') }), barCard({ title: 'Titles', rows: s.titles }));
    } else if (active === 'oath') {
      content = grid(barCard({ title: 'Oath sets', rows: s.oathSets, icon: itemIcon('oath') }), barCard({ title: 'Oath set and option', rows: s.oathSetOptions }));
    } else {
      content = grid(distinctCard(s), exaltedCard(s), blackFangCard(s));
    }

    app.replaceChildren(
      h('div', { class: 'crumbs' }, h('a', { href: '#/' }, 'All classes'), ' / ', title),
      h('div', { class: 'page-head' },
        h('div', { class: 'titles' }, h('h1', {}, title)),
        h('div', { class: 'hero-figure' }, h('div', { class: 'value' }, num(s.sample)), h('div', { class: 'label' }, 'characters'))),
      tabs(`/group/${id}`, tabList, active),
      content);
  }

  function renderClass(key, tab = 'gear') {
    const info = classByKey.get(key), s = D.stats[key];
    if (!info || !s) return renderNotFound();
    document.title = `${info.display} \u00B7 DFO Class Statistics`;

    const tabList = [{ id: 'gear', label: 'Gear' }, { id: 'oath', label: 'Oath' }, { id: 'skills', label: 'Skills' }, { id: 'avatar', label: 'Avatar & Emblems' }];
    const active = tabList.some((t) => t.id === tab) ? tab : 'gear';

    let content;
    if (active === 'gear') {
      content = h('div', {},
        grid(barCard({ title: 'Gear sets', rows: s.sets, icon: itemIcon('sets') }), barCard({ title: 'Weapons', rows: s.weapons, icon: itemIcon('weapons') }), barCard({ title: 'Titles', rows: s.titles })),
        sectionTitle('Special pieces'),
        grid(distinctCard(s), exaltedCard(s), blackFangCard(s)));
    } else if (active === 'oath') {
      content = grid(barCard({ title: 'Oath sets', rows: s.oathSets, icon: itemIcon('oath') }), barCard({ title: 'Oath set and option', rows: s.oathSetOptions }));
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
        h('div', { class: 'hero-figure' }, h('div', { class: 'value' }, num(s.sample)), h('div', { class: 'label' }, `characters \u00B7 ${pct(info.share)} of all`))),
      tabs(`/class/${key}`, tabList, active),
      content);
  }

  // ---------- skills tab ----------

  // Skill icon with an optional corner badge; text tile when there is no icon.
  function skillIcon(skill, badge, label) {
    const wrap = h('span', { class: 'skill-icon', title: label || (skill ? skill.name : '') });
    if (skill && skill.icon) wrap.append(h('img', { src: `img/skills/${skill.id}.png`, alt: skill.name, loading: 'lazy' }));
    else wrap.append(h('span', { class: 'skill-fallback' }, ((skill && skill.name) || '?').slice(0, 3)));
    if (badge) wrap.append(h('img', { class: 'skill-badge', src: `img/skills/badges/${badge}.png`, alt: '' }));
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

    const head = h('tr', {}, h('th', {}, 'Skill'), headings.map((text, i) => h('th', { class: 'num' }, h('span', { class: 'opt-head' }, h('img', { src: `img/skills/badges/${badges[i]}.png`, alt: '' }), text))));
    const body = h('tbody', {}, ordered.map((e) => {
      const lead = e.values[0] >= e.values[1] ? 0 : 1;
      return h('tr', {},
        h('td', {}, h('div', { class: 'skill-name' }, skillIcon(byName.get(e.name)), h('div', {}, h('div', {}, e.name), h('div', { class: 'sub' }, pct(e.values[0] + e.values[1]))))),
        e.values.map((v, i) => h('td', { class: `num${i === lead && v >= 50 ? ' lead' : ''}` }, pct(v))));
    }));
    return h('section', { class: 'card' }, h('h3', {}, title), h('table', { class: 'option-table' }, h('thead', {}, head), body));
  }

  // Skill tree: a row per required level, each skill with adoption rate and average level.
  function skillTreeCard(tree, ratesList) {
    const rates = new Map(ratesList.map((r) => [r.skill, r]));
    const shown = tree.filter((sk) => sk.icon || (rates.get(sk.name) && rates.get(sk.name).takePercent > 0));
    const levels = [...new Set(shown.map((sk) => sk.level))].sort((a, b) => a - b);

    const rows = levels.map((level) => {
      const cells = shown.filter((sk) => sk.level === level)
        .sort((a, b) => (a.type === b.type ? 0 : a.type === 'active' ? -1 : 1))
        .map((sk) => {
          const r = rates.get(sk.name);
          const take = r ? r.takePercent : 0;
          const tone = take >= 99.5 ? 'full' : take >= 50 ? 'mid' : take > 0 ? 'low' : 'none';
          return h('div', { class: `tree-cell ${tone}`, title: sk.name },
            skillIcon(sk),
            h('div', { class: 'take' }, take.toFixed(1)),
            h('div', { class: 'avg' }, r ? r.averageLevelTakers.toFixed(1) : '0'));
        });
      return h('div', { class: 'tree-row' }, h('div', { class: 'tree-level' }, String(level)), h('div', { class: 'tree-cells' }, cells));
    });
    return h('section', { class: 'card wide' }, h('h3', {}, 'Skill tree'), h('div', { class: 'tree' }, rows));
  }
  function renderNotFound() {
    app.replaceChildren(h('h1', {}, 'Not found'), h('a', { class: 'chip gold', href: '#/' }, 'Back to all classes'));
  }

  // ---------- routing ----------

  function route() {
    hideTip();
    const parts = (location.hash.replace(/^#\/?/, '') || '').split('/').filter(Boolean);
    if (parts.length === 0) renderHome();
    else if (parts[0] === 'group') renderGroup(parts[1], parts[2]);
    else if (parts[0] === 'class') renderClass(parts[1], parts[2]);
    else renderNotFound();
    window.scrollTo(0, 0);
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


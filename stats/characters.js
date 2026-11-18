// Leaderboards (every character above the fame floor, paged) and character pages (data/characters/...). app.js calls render() for the
// #/characters/<list>/<page> and #/character/<class>/<id> routes.
(() => {
  'use strict';

  const GEAR_ORDER = ['WEAPON', 'TITLE', 'JACKET', 'SHOULDER', 'PANTS', 'SHOES', 'WAIST', 'AMULET', 'WRIST', 'RING', 'SUPPORT', 'MAGIC_STON', 'EARRING'];
  const GEAR_LABEL = {
    WEAPON: 'Weapon', TITLE: 'Title', JACKET: 'Top', SHOULDER: 'Head/Shoulder', PANTS: 'Bottom', SHOES: 'Shoes', WAIST: 'Belt',
    AMULET: 'Necklace', WRIST: 'Bracelet', RING: 'Ring', SUPPORT: 'Sub Equipment', MAGIC_STON: 'Magic Stone', EARRING: 'Earrings',
  };
  const AVATAR_ORDER = ['HEADGEAR', 'HAIR', 'FACE', 'JACKET', 'BREAST', 'PANTS', 'WAIST', 'SHOES', 'WEAPON', 'SKIN', 'AURORA', 'AURA_SKIN'];
  const AVATAR_LABEL = {
    HEADGEAR: 'Hat', HAIR: 'Hair', FACE: 'Face', JACKET: 'Top', BREAST: 'Torso', PANTS: 'Bottom', WAIST: 'Belt', SHOES: 'Shoes',
    WEAPON: 'Weapon', SKIN: 'Skin', AURORA: 'Aura', AURA_SKIN: 'Aura Skin',
  };
  const TABS = [{ id: 'gear', label: 'Gear' }, { id: 'skills', label: 'Skills' }, { id: 'avatar', label: 'Avatar & Creature' }];

  const cache = new Map();

  // The exporter stores every string once per file and replaces it by -(index + 1); the data holds no other negative numbers.
  function unpack(node, strings) {
    if (typeof node === 'number') return node < 0 ? strings[-node - 1] : node;
    if (Array.isArray(node)) return node.map((x) => unpack(x, strings));
    if (node && typeof node === 'object') return Object.fromEntries(Object.entries(node).map(([k, v]) => [k, unpack(v, strings)]));
    return node;
  }

  // One data file (a script that fills window.DFO_CHARS[path]), loaded once. Returns { size, total, data } or, for the id lists, the plain object.
  function load(path) {
    if (cache.has(path)) return Promise.resolve(cache.get(path));
    return new Promise((resolve, reject) => {
      const script = document.createElement('script');
      script.src = `data/characters/${path.split('/').map(encodeURIComponent).join('/')}.js`;
      script.onload = () => {
        const raw = window.DFO_CHARS[path];
        const file = raw.s ? { size: raw.size, total: raw.total, data: unpack(raw.d, raw.s) } : raw;
        cache.set(path, file);
        resolve(file);
      };
      script.onerror = () => reject(new Error('This page does not exist.'));
      document.head.append(script);
    });
  }

  const cap = (text) => (text ? text[0].toUpperCase() + text.slice(1) : '');
  const byOrder = (order, key) => { const i = order.indexOf(key); return i < 0 ? 99 : i; };

  function render(ui, parts) {
    const here = location.hash;
    const show = (promise) => promise.catch((e) => {
      if (location.hash === here) ui.app.replaceChildren(ui.h('div', { class: 'empty' }, e.message));
    });
    if (parts[0] === 'characters') show(renderLeaderboard(ui, parts[1] || 'all', Math.max(1, parseInt(parts[2], 10) || 1), here));
    else show(renderCharacter(ui, parts[1], parts[2], parts[3], here));
  }

  // ---------- pieces ----------

  // Item icon in a rarity frame, with the reinforce or amplification level in the corner; hovering shows the item's details.
  function itemBox(ui, item, extra = '') {
    const { h } = ui;
    const level = item.reinforce ? h('span', { class: `cbadge${item.amp ? ' amp' : ''}` }, `+${item.reinforce}`) : null;
    const tune = item.tune ? h('span', { class: 'cbadge' }, `T${item.tune}`) : null;
    const stands = item.level ? h('span', { class: 'clevel' }, String(item.level)) : null;
    const box = h('span', { class: `cicon item-icon rarity-${(item.rarity || 'common').toLowerCase()} ${extra}`.trim(), tabindex: '0' },
      item.icon ? h('img', { src: `img/items/${item.icon}`, alt: item.name, loading: 'lazy' }) : h('span', { class: 'cfallback' }, item.name.slice(0, 2)),
      level || tune, stands);
    const open = (e) => itemTip(ui, e, item);
    box.addEventListener('pointermove', open);
    box.addEventListener('focus', open);
    box.addEventListener('pointerleave', ui.hideTip);
    box.addEventListener('blur', ui.hideTip);
    return box;
  }

  function itemTip(ui, event, item) {
    const { h } = ui;
    const lines = [];
    if (item.reinforce) lines.push(h('div', { class: 'td' }, item.amp ? `+${item.reinforce} ${item.amp}` : `+${item.reinforce} Reinforce`));
    if (item.refine) lines.push(h('div', { class: 'td' }, `Refine ${item.refine}`));
    if (item.tune) lines.push(h('div', { class: 'td' }, `Tune ${item.tune}`));
    for (const e of item.enchant || []) lines.push(h('div', { class: 'td' }, e));
    if (item.set) lines.push(h('div', { class: 'td' }, item.set));
    ui.tip.replaceChildren(h('div', { class: 'tn strong' }, item.name), item.rarity ? h('div', { class: 'td' }, item.rarity) : null, ...lines);
    ui.tip.hidden = false;
    ui.moveTip(event);
  }

  // A leaderboard row's icon grid, 14 columns: weapon and the other gear slots over the oath item and its crystals (so each oath piece sits
  // under a gear slot), then Title and Pet over the weapon avatar and Aura. A missing item leaves its cell empty.
  // row.cells is positional (CharacterCards.Cells in the exporter): 0-11 gear, 12 title, 13 pet, 14 oath item, 15-25 crystals, 26 weapon avatar, 27 aura;
  // a cell is [name, icon, rarity, reinforce, amp, tune, level] (trailing nulls cut off) or null.
  function itemGrid(ui, row) {
    const boxes = [];
    const place = (index, column, line, extra = '') => {
      const cell = row.cells[index];
      if (!cell) return;
      const [name, icon, rarity, reinforce, amp, tune, level] = cell;
      const box = itemBox(ui, { name, icon, rarity, reinforce, amp, tune, level }, extra);
      box.style.gridColumn = String(column);
      box.style.gridRow = String(line);
      boxes.push(box);
    };
    for (let i = 0; i < 12; i++) { place(i, i + 1, 1); place(14 + i, i + 1, 2); }
    place(12, 13, 1);
    place(13, 14, 1);
    place(26, 13, 2, 'plain');
    place(27, 14, 2, 'plain');
    return ui.h('div', { class: 'item-grid' }, boxes);
  }

  // Previous / numbered pages / next, around the current page (the first and last page are always there).
  function pager(ui, base, page, pages) {
    const { h } = ui;
    const link = (n, label, cls = '') => h('a', { class: `page-link ${cls}`.trim(), href: `#${base}/${n}` }, label ?? String(n));
    const items = [page > 1 ? link(page - 1, '‹ Prev', 'step') : h('span', { class: 'page-link step off' }, '‹ Prev')];
    let last = 0;
    for (let n = 1; n <= pages; n++) {
      if (n !== 1 && n !== pages && Math.abs(n - page) > 2) continue;
      if (n - last > 1) items.push(h('span', { class: 'page-gap' }, '…'));
      items.push(n === page ? h('span', { class: 'page-link current', 'aria-current': 'page' }, String(n)) : link(n));
      last = n;
    }
    items.push(page < pages ? link(page + 1, 'Next ›', 'step') : h('span', { class: 'page-link step off' }, 'Next ›'));
    return h('nav', { class: 'pager', 'aria-label': 'Pages' }, items);
  }

  const gearOf = (c) => [...c.gear].sort((a, b) => byOrder(GEAR_ORDER, a.slot) - byOrder(GEAR_ORDER, b.slot));
  const classOf = (ui, c) => ui.classByKey.get(c.class);

  // ---------- leaderboard ----------

  async function renderLeaderboard(ui, key, page, here) {
    const { h, num, D } = ui;
    const group = ui.groups[key];
    const info = group ? null : ui.classByKey.get(key);
    if (!group && !info) return ui.renderNotFound();
    const heading = group || info.display;
    document.title = `Leaderboard · ${heading} · DFO Class Statistics`;

    if (!cache.has(`${key}/${page - 1}`)) ui.app.replaceChildren(h('div', { class: 'empty' }, 'Loading…'));
    const file = await load(`${key}/${page - 1}`);
    if (location.hash !== here) return;
    const chars = file.data;
    const pages = Math.ceil(file.total / file.size);
    const first = (page - 1) * file.size;

    // Class picker, grouped by base class.
    const picker = h('select', { class: 'class-select', 'aria-label': 'Class' }, ...Object.entries(ui.groups).map(([id, name]) => h('option', { value: id }, name)));
    const bases = new Map();
    for (const c of D.classes) { if (!bases.has(c.baseClass)) bases.set(c.baseClass, []); bases.get(c.baseClass).push(c); }
    [...bases.keys()].sort((a, b) => ui.baseIndex(a) - ui.baseIndex(b)).forEach((base) => {
      picker.append(h('optgroup', { label: base }, bases.get(base).sort((a, b) => a.display.localeCompare(b.display)).map((c) => h('option', { value: c.key }, c.display))));
    });
    picker.value = key;
    picker.addEventListener('change', () => { location.hash = `#/characters/${picker.value}`; });

    const rows = chars.map((c, i) => {
      const cls = classOf(ui, c);
      return h('a', { class: 'lb-row', href: `#/character/${c.class}/${c.id}` },
        h('div', { class: 'lb-rank' }, String(first + i + 1)),
        h('div', { class: 'lb-who' },
          cls && cls.portrait ? h('img', { class: 'lb-class', src: `img/classes/${cls.portrait}`, alt: '', loading: 'lazy' }) : null,
          h('div', {}, h('div', { class: 'lb-name' }, c.name), h('div', { class: 'lb-sub' }, [cap(c.server), c.level ? `Lv. ${c.level}` : null, cls ? cls.display : null].filter(Boolean).join(' · ')))),
        h('div', { class: 'lb-fame' }, num(c.fame)),
        h('div', { class: 'lb-items' }, itemGrid(ui, c)));
    });

    const base = `/characters/${key}`;
    const bar = () => h('div', { class: 'pager-row' }, pager(ui, base, page, pages), h('div', { class: 'pager-count' }, `${num(first + 1)}–${num(first + chars.length)} of ${num(file.total)}`));
    ui.app.replaceChildren(
      h('div', { class: 'crumbs' }, h('a', { href: '#/characters' }, 'Leaderboard'), ` / ${heading}`),
      h('div', { class: 'lb-head' }, h('h1', {}, heading), picker),
      bar(),
      h('div', { class: 'lb-list' }, rows),
      bar());
  }

  // ---------- character page ----------

  async function renderCharacter(ui, key, id, tab, here) {
    const { h, num } = ui;
    const info = ui.classByKey.get(key);
    if (!info) return ui.renderNotFound();
    ui.app.replaceChildren(h('div', { class: 'empty' }, 'Loading…'));
    // The class's id list says which chunk of full cards holds the character; that chunk is also the leaderboard page they are on.
    const ids = await load(`${key}/ids`);
    const position = ids.ids.indexOf(id);
    if (position < 0) { if (location.hash === here) ui.renderNotFound(); return; }
    const chunk = Math.floor(position / ids.size);
    const c = (await load(`${key}/d${chunk}`)).data.find((x) => x.id === id);
    if (location.hash !== here) return;
    if (!c) return ui.renderNotFound();
    document.title = `${c.name} · DFO Class Statistics`;

    const active = TABS.some((t) => t.id === tab) ? tab : 'gear';
    const content = active === 'gear' ? gearTab(ui, c) : active === 'skills' ? skillsTab(ui, c, info) : avatarTab(ui, c);

    ui.app.replaceChildren(
      h('div', { class: 'crumbs' }, h('a', { href: '#/characters' }, 'Leaderboard'), ' / ', h('a', { href: `#/characters/${key}/${chunk + 1}` }, info.display), ' / ', c.name),
      h('div', { class: 'page-head' },
        h('div', { class: 'portrait' }, info.portrait ? h('img', { src: `img/classes/${info.portrait}`, alt: `${info.display} portrait` }) : null),
        h('div', { class: 'titles' },
          h('h1', {}, c.name),
          h('div', { class: 'chips' }, h('span', { class: 'chip' }, cap(c.server)), c.level ? h('span', { class: 'chip' }, `Lv. ${c.level}`) : null,
            h('a', { class: 'chip gold', href: `#/class/${key}` }, info.display))),
        h('div', { class: 'hero-figure' }, h('div', { class: 'value' }, num(c.fame)), h('div', { class: 'label' }, 'fame'))),
      ui.tabs(`/character/${key}/${id}`, TABS, active),
      content);
  }

  function gearTab(ui, c) {
    const { h } = ui;
    const row = (item, label) => h('div', { class: 'gear-row' },
      itemBox(ui, item),
      h('div', { class: 'gear-text' },
        h('div', { class: 'gear-slot' }, label),
        h('div', { class: 'gear-name' }, item.name),
        item.amp || item.reinforce || item.refine ? h('div', { class: 'gear-sub' }, [item.reinforce ? (item.amp ? `+${item.reinforce} ${item.amp}` : `+${item.reinforce}`) : null, item.refine ? `Refine ${item.refine}` : null].filter(Boolean).join(' · ')) : null,
        item.enchant && item.enchant.length ? h('div', { class: 'gear-sub' }, item.enchant.join(', ')) : null));

    const gear = h('section', { class: 'card wide' }, h('h3', {}, 'Equipment'),
      h('div', { class: 'gear-grid' }, gearOf(c).map((item) => row(item, GEAR_LABEL[item.slot] || item.slot))));

    const oath = c.oath;
    const oathCard = oath ? h('section', { class: 'card wide' }, h('h3', {}, 'Oath'),
      oath.set ? h('div', { class: 'oath-head' }, h('span', { class: 'gear-name' }, oath.set), oath.option ? h('span', { class: 'chip gold' }, oath.option) : null, oath.setRarity ? h('span', { class: 'chip' }, oath.setRarity) : null) : null,
      h('div', { class: 'icon-line big' }, oath.item ? itemBox(ui, oath.item, 'oath-main') : null, oath.crystals.map((item) => itemBox(ui, item))),
      h('div', { class: 'crystal-names' }, oath.item ? h('div', { class: 'gear-sub' }, oath.item.name) : null)) : null;
    return h('div', {}, gear, oathCard);
  }

  function skillsTab(ui, c, info) {
    const { h } = ui;
    const tree = ui.D.skillTree[info.key] || [];
    const byId = new Map(tree.map((s) => [s.id, s]));
    const { levels, vp, enhancement } = c.skills;
    const badgeOf = (id) => (vp[id] ? `Evolve${vp[id]}` : enhancement[id] ? `Enhance${enhancement[id]}` : null);

    const pickList = (title, picks, label, badge) => {
      const entries = Object.entries(picks).map(([id, type]) => [byId.get(id), type]).filter(([s]) => s)
        .sort((a, b) => a[0].level - b[0].level);
      return h('section', { class: 'card' }, h('h3', {}, title),
        entries.length ? h('ul', { class: 'pick-list' }, entries.map(([s, type]) => h('li', {}, ui.skillIcon(s, badge(type)), h('div', {}, h('div', { class: 'gear-name' }, s.name), h('div', { class: 'gear-sub' }, label(type))))))
          : h('div', { class: 'empty' }, 'None.'));
    };

    const shown = tree.filter((s) => s.icon || levels[s.id]);
    const layout = ui.layoutTree(shown);
    const rows = layout.rows.map(({ level, cells }, rowIndex) => h('div', { class: 'tree-row' },
      h('div', { class: 'tree-level' }, String(level)),
      h('div', { class: 'tree-cells', style: `grid-template-columns: repeat(${layout.columns}, 56px)` }, cells.map(({ skill: s, col }) => {
        const lv = levels[s.id] || 0;
        const pre = (s.pre || []).filter((id) => shown.some((x) => x.id === id));
        return h('div', { class: `tree-cell ${lv ? 'full' : 'none'}`, title: s.name, style: `grid-column: ${col + 1}; grid-row: 1`, 'data-id': s.id, 'data-row': rowIndex, 'data-pre': pre.length ? pre.join(',') : null },
          ui.skillIcon(s, badgeOf(s.id)), h('div', { class: 'take' }, lv ? String(lv) : '0'));
      }))));
    const svg = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
    svg.setAttribute('class', 'tree-links');
    const box = h('div', { class: 'tree' }, svg, rows);
    new ResizeObserver(() => ui.drawTreeLinks(box, svg)).observe(box);

    return h('div', {},
      h('div', { class: 'card-grid' },
        pickList('VP', vp, (t) => `VP ${t === 1 ? 'I' : 'II'}`, (t) => `Evolve${t}`),
        pickList('Enhancement', enhancement, (t) => (t === 1 ? 'Damage' : 'Cooldown'), (t) => `Enhance${t}`)),
      h('section', { class: 'card wide' }, h('h3', {}, 'Skill tree'), box));
  }

  function avatarTab(ui, c) {
    const { h } = ui;
    const colors = { ...ui.emblemColors, Platinum: '#CFD8E3' };
    const emblemBox = (e) => (e.icon
      ? h('span', { class: 'cicon emblem', title: e.name }, h('img', { src: `img/items/${e.icon}`, alt: e.name, loading: 'lazy' }))
      : h('i', { class: 'dot', title: e.name, style: `background:${colors[e.color] || '#6D727A'}` }));

    const creature = c.creature ? h('section', { class: 'card' }, h('h3', {}, 'Creature'),
      h('div', { class: 'gear-row' }, itemBox(ui, c.creature.item), h('div', { class: 'gear-text' }, h('div', { class: 'gear-name' }, c.creature.item.name),
        c.creature.twin ? h('div', { class: 'gear-sub' }, `Clone: ${c.creature.twin.name}`) : null)),
      c.creature.artifacts.length ? h('div', { class: 'icon-line' }, c.creature.artifacts.map((a) => itemBox(ui, a))) : null) : null;

    const avatars = [...c.avatar].sort((a, b) => byOrder(AVATAR_ORDER, a.slot) - byOrder(AVATAR_ORDER, b.slot));
    const avatarCard = h('section', { class: 'card wide' }, h('h3', {}, 'Avatar'),
      h('div', { class: 'gear-grid' }, avatars.map((a) => h('div', { class: 'gear-row' },
        itemBox(ui, a.item, 'plain'),
        h('div', { class: 'gear-text' }, h('div', { class: 'gear-slot' }, AVATAR_LABEL[a.slot] || a.slot), h('div', { class: 'gear-name' }, a.item.name),
          a.emblems.length ? h('div', { class: 'emblem-line' }, a.emblems.map(emblemBox)) : null)))));
    return h('div', {}, creature, avatarCard);
  }

  window.DFO_CHARACTERS = { render };
})();

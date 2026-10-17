// Recomputes one class's statistics from its character rows, so the page can filter (by set, oath set, fame).
// This mirrors Collector/Stats.cs `Compute` and the export's trimming; the row layout is the one SiteRecords.cs writes.
(() => {
  'use strict';

  // Column of each field in a row.
  const I = { FAME: 0, SET: 1, OATH: 2, OPTION: 3, WEAPON: 4, TITLE: 5, DISTINCT: 6, EXALTED: 7, BLACK_FANG: 8, SKILLS: 9,
    VP: 10, ENHANCEMENT: 11, VP_SET: 12, ENHANCEMENT_SET: 13, CREATURE: 14, CLONE: 15, PLATINUM: 16, EMBLEMS: 17 };

  const TOP = 20;
  const TOP_LONG = 80;        // tables that need every row (each skill appears once per option)
  const BLACK_FANG_SLOTS = 3;
  const OATH_BOXES = 3;       // oath sets shown with their options
  const EMBLEM_COLORS = ['Red', 'Yellow', 'Green', 'Blue', 'Multicolored'];

  const percent = (part, total) => (total === 0 ? 0 : (100 * part) / total);

  // [flat array of pairs] -> [[a, b], ...]
  const pairs = (flat) => {
    const out = [];
    for (let i = 0; i < flat.length; i += 2) out.push([flat[i], flat[i + 1]]);
    return out;
  };

  // How often each value occurs, most common first; equal counts keep the order in which they were first seen.
  function rates(values, total) {
    const counts = new Map();
    for (const v of values) counts.set(v, (counts.get(v) || 0) + 1);
    return [...counts].map(([name, count]) => ({ name, count, percent: (100 * count) / total })).sort((a, b) => b.count - a.count);
  }

  // Percent within each socket color, colors in game order.
  function emblemEffects(rows, text) {
    const counts = new Map();
    for (const r of rows) for (const [key, count] of pairs(r[I.EMBLEMS])) counts.set(text(key), (counts.get(text(key)) || 0) + count);
    const byColor = new Map();
    for (const [key, count] of counts) {
      const [color, effect] = key.split('|');
      if (!byColor.has(color)) byColor.set(color, []);
      byColor.get(color).push({ effect, count });
    }
    return [...byColor].sort((a, b) => EMBLEM_COLORS.indexOf(a[0]) - EMBLEM_COLORS.indexOf(b[0])).flatMap(([color, list]) => {
      const total = list.reduce((sum, x) => sum + x.count, 0);
      return list.sort((a, b) => b.count - a.count).map((x) => ({ name: `${color}: ${x.effect}`, count: x.count, percent: (100 * x.count) / total }));
    });
  }

  // The most used oath sets, each with its options' rates within that set. items: [{ set, option }] of the oath wearers.
  function oathBreakdown(items) {
    return rates(items.map((i) => i.set), items.length).slice(0, OATH_BOXES).map((set) => ({
      set: set.name,
      count: set.count,
      percent: set.percent,
      options: rates(items.filter((i) => i.set === set.name).map((i) => i.option), set.count),
    }));
  }

  // Statistics that mean the same in every class, over several classes at once. parts: [{ display, table, rows }] (rows already filtered).
  function computeGroupStats(parts) {
    const all = parts.flatMap((p) => p.rows.map((r) => ({ r, cls: p.display, text: (i) => p.table[i] })));
    const n = all.length;
    const withOath = all.filter((x) => x.text(x.r[I.OATH]) !== '(none)');
    const blackFang = all.filter((x) => x.r[I.BLACK_FANG] === 0 || x.r[I.BLACK_FANG] === BLACK_FANG_SLOTS);
    const top = (list, take = TOP) => list.slice(0, take);
    return {
      sample: n,
      classShare: rates(all.map((x) => x.cls), n),
      sets: top(rates(all.map((x) => x.text(x.r[I.SET])), n), TOP_LONG),
      oathSample: withOath.length,
      oathSets: top(rates(withOath.map((x) => x.text(x.r[I.OATH])), withOath.length)),
      oathBreakdown: oathBreakdown(withOath.map((x) => ({ set: x.text(x.r[I.OATH]), option: x.text(x.r[I.OPTION]) }))),
      titles: top(rates(all.map((x) => x.text(x.r[I.TITLE])), n)),
      distinctBySlot: top(rates(all.flatMap((x) => x.r[I.DISTINCT].map(x.text)), n)),
      anyDistinctPercent: percent(all.filter((x) => x.r[I.DISTINCT].length > 0).length, n),
      exalted: top(rates(all.flatMap((x) => x.r[I.EXALTED].map(x.text)), n)),
      blackFangSample: blackFang.length,
      blackFangAveragePercent: percent(blackFang.reduce((sum, x) => sum + x.r[I.BLACK_FANG], 0), BLACK_FANG_SLOTS * blackFang.length),
      blackFangCounts: top(rates(blackFang.map((x) => `${x.r[I.BLACK_FANG]} of ${BLACK_FANG_SLOTS}`), blackFang.length)),
      blackFangSets: top(rates(blackFang.filter((x) => x.r[I.BLACK_FANG] === BLACK_FANG_SLOTS).map((x) => x.text(x.r[I.SET])), blackFang.length)),
    };
  }

  // table: the class's string table; rows: the characters to count. Returns the same shape as the exported class statistics.
  function computeClassStats(table, rows) {
    const text = (i) => table[i];
    const n = rows.length;
    const withOath = rows.filter((r) => text(r[I.OATH]) !== '(none)');
    const blackFang = rows.filter((r) => r[I.BLACK_FANG] === 0 || r[I.BLACK_FANG] === BLACK_FANG_SLOTS);
    const clones = rows.filter((r) => r[I.CLONE] !== -1);

    // A skill missing from a character's list counts as level 0 for the "all" average.
    const bySkill = new Map();
    for (const r of rows) {
      for (const [skill, level] of pairs(r[I.SKILLS])) {
        const entry = bySkill.get(skill) || { count: 0, sum: 0 };
        entry.count++;
        entry.sum += level;
        bySkill.set(skill, entry);
      }
    }
    const skills = [...bySkill].map(([skill, e]) => ({ skill: text(skill), takePercent: (100 * e.count) / n, averageLevelAll: e.sum / n, averageLevelTakers: e.sum / e.count }))
      .sort((a, b) => b.takePercent - a.takePercent);

    const platinum = (slot) => {
      const skills = rows.flatMap((r) => pairs(r[I.PLATINUM]).filter(([s]) => text(s) === slot).map(([, skill]) => text(skill)));
      return rates(skills, skills.length);
    };

    const top = (list, take = TOP) => list.slice(0, take);
    return {
      sample: n,
      sets: top(rates(rows.map((r) => text(r[I.SET])), n)),
      oathSample: withOath.length,
      oathSets: top(rates(withOath.map((r) => text(r[I.OATH])), withOath.length)),
      oathBreakdown: oathBreakdown(withOath.map((r) => ({ set: text(r[I.OATH]), option: text(r[I.OPTION]) }))),
      weapons: top(rates(rows.map((r) => text(r[I.WEAPON])), n)),
      titles: top(rates(rows.map((r) => text(r[I.TITLE])), n)),
      distinctBySlot: top(rates(rows.flatMap((r) => r[I.DISTINCT].map(text)), n)),
      anyDistinctPercent: percent(rows.filter((r) => r[I.DISTINCT].length > 0).length, n),
      exalted: top(rates(rows.flatMap((r) => r[I.EXALTED].map(text)), n)),
      blackFangSample: blackFang.length,
      blackFangAveragePercent: percent(blackFang.reduce((sum, r) => sum + r[I.BLACK_FANG], 0), BLACK_FANG_SLOTS * blackFang.length),
      blackFangCounts: top(rates(blackFang.map((r) => `${r[I.BLACK_FANG]} of ${BLACK_FANG_SLOTS}`), blackFang.length)),
      blackFangSets: top(rates(blackFang.filter((r) => r[I.BLACK_FANG] === BLACK_FANG_SLOTS).map((r) => text(r[I.SET])), blackFang.length)),
      vpSkills: top(rates(rows.flatMap((r) => pairs(r[I.VP]).map(([skill, type]) => `${text(skill)} (VP${type})`)), n), TOP_LONG),
      vpSets: top(rates(rows.map((r) => text(r[I.VP_SET])), n)),
      enhancementPicks: top(rates(rows.flatMap((r) => pairs(r[I.ENHANCEMENT]).map(([skill, type]) => `${text(skill)} (${type === 1 ? 'damage' : 'cooldown'})`)), n), TOP_LONG),
      enhancementSets: top(rates(rows.map((r) => text(r[I.ENHANCEMENT_SET])), n)),
      weaponAvatarSample: clones.length,
      weaponAvatarLevels: top(rates(clones.map((r) => `Rare Clone [${r[I.CLONE]}Lv]`), clones.length)),
      creatures: top(rates(rows.map((r) => text(r[I.CREATURE])), n)),
      platinumTop: top(platinum('JACKET')),
      platinumBottom: top(platinum('PANTS')),
      platinumAura: top(platinum('AURORA')),
      emblemEffects: top(emblemEffects(rows, text), TOP_LONG),
      skills,
    };
  }

  // filter = { sets: Set of set families, oaths: Set of oath set names, min, max } (an empty Set or a null limit means "any").
  // familyOf(setName) gives the family a set name belongs to.
  function filterRows(table, rows, familyOf, filter) {
    return rows.filter((r) =>
      (filter.sets.size === 0 || filter.sets.has(familyOf(table[r[I.SET]])))
      && (filter.oaths.size === 0 || filter.oaths.has(table[r[I.OATH]]))
      && (filter.min == null || r[I.FAME] >= filter.min)
      && (filter.max == null || r[I.FAME] <= filter.max));
  }

  window.DFO_FILTER = { I, computeClassStats, computeGroupStats, filterRows };
})();

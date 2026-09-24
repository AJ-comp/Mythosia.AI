// Run with Node.js: node build/generate-architecture.mjs
// Optional PNG export: node build/generate-architecture.mjs --png (requires sharp).
// This is a package map grouped by responsibility, not a dependency graph.
// The expandable Mermaid in the READMEs retains the exact package references.
import { mkdirSync, writeFileSync } from 'node:fs';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { createRequire } from 'node:module';

const root = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const output = resolve(root, 'docs/assets');
mkdirSync(output, { recursive: true });
const W = 1600, H = 1120;
const themes = {
  light: {
    bg: '#FFFFFF', card: '#FFFFFF', ink: '#172C46', muted: '#68788E', border: '#DCE4EE',
    blue: '#4D6FA5', blueWash: '#F0F5FC', teal: '#3C7D79', tealWash: '#F0F7F5',
    violet: '#7C68A0', violetWash: '#F5F2FA', amber: '#8C744C', amberWash: '#FAF6EF',
    slate: '#66758D', slateWash: '#F3F5F8', badge: '#E6F0EC', badgeInk: '#326C60',
  },
  dark: {
    bg: '#0D1522', card: '#1A283A', ink: '#EDF3FC', muted: '#A8B8CD', border: '#35475D',
    blue: '#A1BBE5', blueWash: '#15243A', teal: '#9ACEC3', tealWash: '#142C2D',
    violet: '#C2B0DF', violetWash: '#272138', amber: '#D0BE9B', amberWash: '#2D291F',
    slate: '#ACBBD0', slateWash: '#1C2433', badge: '#24483F', badgeInk: '#B7E5D8',
  },
};
const escape = value => String(value).replaceAll('&', '&amp;').replaceAll('<', '&lt;').replaceAll('>', '&gt;').replaceAll('"', '&quot;');

function diagram(mode) {
  const t = themes[mode];
  const out = [];
  const add = value => out.push(value);
  const rect = (x, y, w, h, fill, stroke = 'none', radius = 14) =>
    add(`<rect x="${x}" y="${y}" width="${w}" height="${h}" rx="${radius}" fill="${fill}" stroke="${stroke}"/>`);
  const text = (x, y, value, size = 26, fill = t.ink, weight = 600, extra = '') =>
    add(`<text x="${x}" y="${y}" fill="${fill}" font-size="${size}" font-weight="${weight}" ${extra}>${escape(value)}</text>`);
  const group = (x, y, w, h, label, color) => {
    rect(x, y, w, h, t[`${color}Wash`]);
    text(x + 24, y + 37, label, 20, t[color], 600, 'letter-spacing=".5"');
  };
  const pkg = (x, y, w, name, size = 28, h = 70) => {
    add(`<g data-package="${escape(name)}">`);
    rect(x, y, w, h, t.card, t.border, 10);
    // Full package IDs stay on one line, with no namespace abbreviations.
    text(x + 22, y + h / 2 + 10, name, size, t.ink, 650, 'letter-spacing="-.35"');
    add('</g>');
  };

  add(`<?xml version="1.0" encoding="UTF-8"?>\n<svg xmlns="http://www.w3.org/2000/svg" width="${W}" height="${H}" viewBox="0 0 ${W} ${H}" role="img" aria-labelledby="title description">`);
  add('<title id="title">Mythosia.AI package architecture</title>');
  add('<desc id="description">Sixteen packages grouped by responsibility. Core AI and RAG orchestration: Mythosia.AI and Mythosia.AI.Rag. Provider and tool extensions: Mythosia.AI.Providers.Alibaba and Mythosia.AI.Mcp. Independent server management: Mythosia.AI.Serving.Vllm. Document loaders: Mythosia.Documents.Office and Mythosia.Documents.Pdf. Vector stores and search: Mythosia.VectorDb.InMemory, Mythosia.VectorDb.Postgres, Mythosia.VectorDb.Qdrant, Mythosia.VectorDb.Pinecone, and the source preview Mythosia.AI.Rag.Search.Pixie. Shared abstractions: Mythosia.AI.Abstractions, Mythosia.AI.Rag.Abstractions, Mythosia.Documents.Abstractions, and Mythosia.VectorDb.Abstractions. Each white or dark card is one package. Group position does not imply a direct dependency; see the README dependency diagram for exact references.</desc>');
  add('<g font-family="Segoe UI, Arial, Helvetica, sans-serif">');
  rect(0, 0, W, H, t.bg, 'none', 20);

  text(48, 75, 'Mythosia.AI', 38, t.ink, 650, 'letter-spacing="-1"');
  text(1552, 72, 'PACKAGE ARCHITECTURE', 19, t.muted, 600, 'text-anchor="end" letter-spacing="2"');

  group(48, 122, 1504, 150, 'Core AI & orchestration', 'blue');
  pkg(72, 180, 716, 'Mythosia.AI', 32);
  pkg(812, 180, 716, 'Mythosia.AI.Rag', 32);

  group(48, 296, 992, 150, 'Provider & tool extensions', 'violet');
  pkg(72, 354, 460, 'Mythosia.AI.Providers.Alibaba', 26);
  pkg(556, 354, 460, 'Mythosia.AI.Mcp', 28);

  // Serving is independent; the layout intentionally has no dependency arrows.
  group(1064, 296, 488, 150, 'Serving / control plane · independent', 'amber');
  pkg(1088, 354, 440, 'Mythosia.AI.Serving.Vllm', 27);

  group(48, 470, 488, 322, 'Document loaders', 'slate');
  pkg(72, 552, 440, 'Mythosia.Documents.Office', 27, 80);
  pkg(72, 668, 440, 'Mythosia.Documents.Pdf', 27, 80);

  group(560, 470, 992, 322, 'Vector stores & search', 'teal');
  pkg(584, 528, 460, 'Mythosia.VectorDb.InMemory', 26);
  pkg(1068, 528, 460, 'Mythosia.VectorDb.Postgres', 26);
  pkg(584, 614, 460, 'Mythosia.VectorDb.Qdrant', 26);
  pkg(1068, 614, 460, 'Mythosia.VectorDb.Pinecone', 26);
  pkg(584, 700, 944, 'Mythosia.AI.Rag.Search.Pixie', 28);
  rect(1361, 720, 144, 30, t.badge, 'none', 15);
  text(1433, 741, 'Source preview', 17, t.badgeInk, 600, 'text-anchor="middle"');

  group(48, 816, 1504, 238, 'Shared abstractions', 'slate');
  pkg(72, 874, 716, 'Mythosia.AI.Abstractions', 28);
  pkg(812, 874, 716, 'Mythosia.AI.Rag.Abstractions', 28);
  pkg(72, 960, 716, 'Mythosia.Documents.Abstractions', 28);
  pkg(812, 960, 716, 'Mythosia.VectorDb.Abstractions', 28);

  text(48, 1094, 'One card = one package', 18, t.muted, 400);
  text(1552, 1094, 'Grouped by responsibility · dependency details in README', 18, t.muted, 400, 'text-anchor="end"');
  add('</g></svg>');
  return out.join('\n') + '\n';
}

for (const theme of ['light', 'dark']) {
  const name = theme === 'light' ? 'architecture' : 'architecture-dark';
  const svg = diagram(theme);
  writeFileSync(resolve(output, `${name}.svg`), svg);
  if (process.argv.includes('--png')) {
    const sharp = createRequire(import.meta.url)('sharp');
    // 3200 px export for sharing; SVG is used by the README at any scale.
    await sharp(Buffer.from(svg), { density: 144 }).png().toFile(resolve(output, `${name}.png`));
  }
}
console.log('Architecture artwork generated in docs/assets.');

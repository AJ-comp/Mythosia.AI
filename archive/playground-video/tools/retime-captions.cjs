const fs = require('node:fs');
const path = require('node:path');
const common = require('./common.cjs');

if (process.argv.includes('--help')) {
  console.log('Retime archived caption text to reuse/spotlight/timeline.json. Outputs: artifacts/playground-video/reuse/captions. Does not edit documentation or source captions.');
  process.exit(0);
}
const timeline = JSON.parse(fs.readFileSync(path.join(common.root, 'artifacts/playground-video/reuse/spotlight/timeline.json'), 'utf8'));
const starts = timeline.captionStarts.map(value => Math.round(value * 1000));
const end = Math.floor(timeline.duration * 1000);
if (!starts.length || starts[0] !== 0 || !Number.isFinite(end) || end <= starts.at(-1) ||
    starts.some((value, index) => !Number.isFinite(value) || (index && value <= starts[index-1]))) {
  throw new Error('Caption starts must increase from zero and precede the video end.');
}
const stamp = ms => `${String(Math.floor(ms/3600000)).padStart(2,'0')}:${String(Math.floor(ms/60000)%60).padStart(2,'0')}:${String(Math.floor(ms/1000)%60).padStart(2,'0')}.${String(ms%1000).padStart(3,'0')}`;
const source = path.join(common.archive, 'captions');
const target = common.output('captions');
for (const file of fs.readdirSync(source).filter(name => name.endsWith('.vtt'))) {
  const before = fs.readFileSync(path.join(source, file), 'utf8');
  let index = 0;
  const after = before.replace(/^\d{2}:\d{2}:\d{2}\.\d{3} --> \d{2}:\d{2}:\d{2}\.\d{3}/gm, () => {
    if (index >= starts.length) throw new Error(`Too many cues in ${file}.`);
    const result = `${stamp(starts[index])} --> ${stamp(starts[index+1] ?? end)}`;
    index++;
    return result;
  });
  if (index !== starts.length) throw new Error(`Caption count does not match the timeline: ${file}.`);
  fs.writeFileSync(path.join(target, file), after);
}
console.log(`Wrote retimed captions to ${target}`);

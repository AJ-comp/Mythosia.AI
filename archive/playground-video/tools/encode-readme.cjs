const fs = require('node:fs');
const path = require('node:path');
const cp = require('node:child_process');
const crypto = require('node:crypto');
const common = require('./common.cjs');
if (process.argv.includes('--help')) {
  console.log('Add English typing captions. PLAYGROUND_VIDEO and PLAYGROUND_CAPTIONS select inputs; default video is artifacts/playground-video/reuse/spotlight/playground-demo.mp4, captions are the archived English VTT. CAPTION_FONT defaults to DejaVu Sans. Outputs: artifacts/playground-video/reuse/readme.');
  process.exit(0);
}
const dir = common.output('readme');
const input = path.resolve(process.env.PLAYGROUND_VIDEO || path.join(common.root, 'artifacts/playground-video/reuse/spotlight/playground-demo.mp4'));
const output = path.join(dir, 'Mythosia-Playground-English.mp4');
const { ffmpeg, ffprobe } = common;
const font = process.env.CAPTION_FONT || 'DejaVu Sans';
if (/[\r\n,]/.test(font)) throw new Error('CAPTION_FONT must be a single ASS font-family name.');
const hash = file => crypto.createHash('sha256').update(fs.readFileSync(file)).digest('hex');
const sourceHash = hash(input);
const seconds = value => value.split(':').reduce((total, part) => total * 60 + Number(part), 0);
const vtt = fs.readFileSync(process.env.PLAYGROUND_CAPTIONS || path.join(common.archive, 'captions/playground-demo.vtt'), 'utf8');
const cues = [...vtt.matchAll(/(\d{2}:\d{2}:\d{2}\.\d{3}) --> (\d{2}:\d{2}:\d{2}\.\d{3})[^\r\n]*\r?\n([^\r\n]+)/g)]
  .map(m => ({ start: seconds(m[1]), end: seconds(m[2]), text: m[3] }));
if (!cues.length) throw new Error('No captions found.');
const probe = file => JSON.parse(cp.execFileSync(ffprobe,['-v','error','-show_entries','stream=width,height,codec_name,r_frame_rate:format=duration,size','-of','json',file], { encoding:'utf8' }));
const inputInfo = probe(input);
const inputDuration = Number(inputInfo.format.duration);
if (inputInfo.streams.length !== 1 || inputInfo.streams[0].width !== 1440 || inputInfo.streams[0].height !== 900) throw new Error('Expected one 1440x900 source video stream.');
if (cues.some((cue,index) => cue.start < 0 || cue.end <= cue.start || cue.end > inputDuration + 0.05 || (index > 0 && cue.start < cues[index-1].end))) throw new Error('Caption timeline is invalid for the input video.');
const stamp = s => {
  const centis = Math.round(s * 100);
  return `${Math.floor(centis/360000)}:${String(Math.floor(centis/6000)%60).padStart(2,'0')}:${String(Math.floor(centis/100)%60).padStart(2,'0')}.${String(centis%100).padStart(2,'0')}`;
};
const escape = text => text.replace(/\\/g, '\\\\').replace(/{/g, '\\{').replace(/}/g, '\\}');
const header = `[Script Info]
ScriptType: v4.00+
PlayResX: 1440
PlayResY: 972
WrapStyle: 2
ScaledBorderAndShadow: yes

[V4+ Styles]
Format: Name, Fontname, Fontsize, PrimaryColour, SecondaryColour, OutlineColour, BackColour, Bold, Italic, Underline, StrikeOut, ScaleX, ScaleY, Spacing, Angle, BorderStyle, Outline, Shadow, Alignment, MarginL, MarginR, MarginV, Encoding
Style: Caption,${font},27,&H00FFFFFF,&H00FFFFFF,&HFF000000,&HFF000000,0,0,0,0,100,100,0,0,1,0,0,5,24,24,0,1

[Events]
Format: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text
`;
const events = [];
for (const cue of cues) {
  const units = [...new Intl.Segmenter('en', { granularity: 'grapheme' }).segment(cue.text)].map(x => x.segment);
  const duration = Math.min(1.4, units.length * 0.028, (cue.end - cue.start) * 0.3);
  for (let count = 1; count <= units.length; count++) {
    const start = cue.start + (count - 1) / units.length * duration;
    const end = count === units.length ? cue.end : cue.start + count / units.length * duration;
    // Invisible suffix reserves the final line width, just like the web player.
    const text = `{\\an5\\pos(720,936)\\alpha&H00&}${escape(units.slice(0,count).join(''))}{\\alpha&HFF&}${escape(units.slice(count).join(''))}`;
    events.push(`Dialogue: 0,${stamp(start)},${stamp(end)},Caption,,0,0,0,,${text}`);
  }
}
fs.writeFileSync(path.join(dir, 'captions.ass'), header + events.join('\n') + '\n');
const filter = 'pad=1440:972:0:0:color=0x14352c,ass=captions.ass';
cp.execFileSync(ffmpeg, ['-hide_banner','-loglevel','warning','-y','-i',input,'-vf',filter,'-an','-c:v','libx264','-preset','slow','-crf','20','-pix_fmt','yuv420p','-movflags','+faststart',output], { cwd: dir, stdio: 'inherit' });
const metadata = probe(output);
if (metadata.streams.length !== 1 || metadata.streams[0].width !== 1440 || metadata.streams[0].height !== 972 || Math.abs(Number(metadata.format.duration)-inputDuration) > 0.05) throw new Error('Unexpected video metadata');
if (sourceHash !== hash(input)) throw new Error('Shared source video changed');
for (const [name,time] of [['typing',cues[0].start + Math.min(0.3, (cues[0].end-cues[0].start)/4)],['complete',cues[0].start + (cues[0].end-cues[0].start)*0.8],['final',Math.max(0,inputDuration-1)]]) {
  cp.execFileSync(ffmpeg,['-hide_banner','-loglevel','error','-y','-ss',String(time),'-i',output,'-frames:v','1','-update','1',path.join(dir, name + '.png')]);
}
fs.writeFileSync(path.join(dir,'validation.json'),JSON.stringify({ metadata, sourceHash, outputHash:hash(output), cues:cues.length, events:events.length },null,2));
console.log(JSON.stringify(metadata,null,2));

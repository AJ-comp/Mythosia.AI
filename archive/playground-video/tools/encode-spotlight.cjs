const fs = require('node:fs');
const path = require('node:path');
const cp = require('node:child_process');
const common = require('./common.cjs');
if (process.argv.includes('--help')) {
  console.log('Encode archived spotlight footage, or --recorded for reuse/capture. FFMPEG_PATH/FFPROBE_PATH override executable paths. Output: artifacts/playground-video/reuse/spotlight.');
  process.exit(0);
}
const inputDir = process.argv.includes('--recorded')
  ? path.join(common.root, 'artifacts/playground-video/reuse/capture')
  : path.join(common.archive, 'spotlight');
const dir = common.output('spotlight');
const { ffmpeg, ffprobe } = common;
const raw = path.join(inputDir,'walkthrough.webm');
const final = path.join(dir,'playground-demo.mp4');
const recording = JSON.parse(fs.readFileSync(path.join(inputDir,'recording.json'),'utf8'));
function probe(file) { return JSON.parse(cp.execFileSync(ffprobe,['-v','error','-show_entries','stream=width,height,r_frame_rate,duration:format=duration,size','-of','json',file],{encoding:'utf8'})); }
const rawInfo=probe(raw);
const [num,den]=rawInfo.streams[0].r_frame_rate.split('/').map(Number), fps=num/den;
if(fps!==25) throw new Error('Unexpected capture frame rate: '+fps);
// Read one corner pixel per captured frame. Only the removable leader is magenta.
const pixels=cp.execFileSync(ffmpeg,['-hide_banner','-loglevel','error','-i',raw,'-vf','crop=2:2:0:0','-pix_fmt','rgb24','-f','rawvideo','pipe:1'],{maxBuffer:1024*1024});
let lastLeader=-1;
for(let i=0;i<pixels.length;i+=12) if(pixels[i]>210 && pixels[i+1]<45 && pixels[i+2]>210) lastLeader=i/12;
if(lastLeader<0 || lastLeader>fps*6) throw new Error('Could not calibrate recording leader');
const trim=(lastLeader+1)/fps;
const remaining=Number(rawInfo.format.duration)-trim;
if(Math.abs(remaining-recording.duration)>0.3) throw new Error('Recording/media timeline drift: '+remaining+' vs '+recording.duration);
const seconds='on/30';
const ease=(start)=>`(0.5-0.5*cos(PI*clip((${seconds}-${start})/0.85,0,1)))`;
const zoom='1+'+recording.zooms.map(z=>`${z.scale-1}*(${ease(z.start)}-${ease(z.end)})`).join('+');
const centers=recording.zooms.map(z=>z.y*2);
const centerY=`if(lt(${seconds},${recording.zooms[1].start}),${centers[0]},if(lt(${seconds},${recording.zooms[2].start}),${centers[1]},${centers[2]}))`;
const filter=`fps=30,scale=2880:1800:flags=lanczos,zoompan=z='${zoom}':x='max(0,min(iw-iw/zoom,2320-iw/(2*zoom)))':y='max(0,min(ih-ih/zoom,(${centerY})-ih/(2*zoom)))':d=1:s=1440x900:fps=30`;
fs.writeFileSync(path.join(dir,'video-filter.txt'),filter);
console.log('Encoding calibrated video; leader trim '+trim.toFixed(3)+'s');
cp.execFileSync(ffmpeg,['-hide_banner','-loglevel','warning','-y','-ss',String(trim),'-i',raw,'-vf',filter,'-an','-c:v','libx264','-preset','slow','-crf','19','-pix_fmt','yuv420p','-movflags','+faststart',final],{stdio:'inherit'});
const finalInfo=probe(final);
const duration=Number(finalInfo.format.duration);
if(finalInfo.streams[0].width!==1440 || finalInfo.streams[0].height!==900) throw new Error('Unexpected final dimensions');
if(Math.abs(duration-recording.duration)>0.3) throw new Error('Final duration drift');
const timeline={captionStarts:recording.captionStarts,duration,leaderTrim:trim,width:1440,height:900,zooms:recording.zooms,actionCount:recording.actions.length};
fs.writeFileSync(path.join(dir,'timeline.json'),JSON.stringify(timeline,null,2));
const chooseFocus=selector=>recording.actions.find(action=>action.kind==='focus' && action.selector===selector);
const checks=[
  ['model-highlight',chooseFocus('#model-search').time+0.85],
  ['splitter-zoom-highlight',chooseFocus('#rag-chunker-select .rag-select-trigger').time+1.05],
  ['embedding-zoom-highlight',chooseFocus('#rag-embedding-trigger').time+1.05],
  ['rerank-zoom-highlight',chooseFocus('label:has(#rag-rerank-enabled)').time+1.05],
  ['click-ripple',recording.actions.find(a=>a.kind==='click' && a.selector==='#model-search').time+0.12]
];
for(const [name,time] of checks) cp.execFileSync(ffmpeg,['-hide_banner','-loglevel','error','-y','-ss',String(time),'-i',final,'-frames:v','1','-update','1',path.join(dir,name+'.png')]);
console.log(JSON.stringify({...timeline,size:finalInfo.format.size},null,2));

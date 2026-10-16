const fs = require('node:fs');
const path = require('node:path');

const archive = path.resolve(__dirname, '..');
const root = path.resolve(archive, '../..');
const outputRoot = path.join(root, 'artifacts/playground-video/reuse');

// Archived inputs remain read-only; every generated file goes to ignored artifacts.
function output(name) {
  const directory = path.join(outputRoot, name);
  fs.mkdirSync(directory, { recursive: true });
  return directory;
}

module.exports = {
  archive, root, output,
  ffmpeg: process.env.FFMPEG_PATH || 'ffmpeg',
  ffprobe: process.env.FFPROBE_PATH || 'ffprobe',
};

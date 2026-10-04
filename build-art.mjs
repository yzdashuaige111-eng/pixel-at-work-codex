import fs from 'node:fs';
import path from 'node:path';
import { stripTypeScriptTypes } from 'node:module';
import vm from 'node:vm';
const base = process.argv[2] || process.cwd();
const source = fs.readFileSync(path.join(base, 'upstream/register.tsx'), 'utf8');
const from = source.indexOf("type Lang = 'zh' | 'en'");
const end = source.indexOf('// The one-line animation of the terminal.');
if (from < 0 || end < from) throw new Error('Upstream renderer markers changed');
let adapted = source.slice(from, end);
const creatureFrom = adapted.indexOf('// The little orange creature:');
const creatureEnd = adapted.indexOf('const ENVELOPE', creatureFrom);
const palette = "o: '#d97757', ol: '#f2b49b', eye: '#2b1b17'";
if (creatureFrom < 0 || creatureEnd < creatureFrom || !adapted.includes(palette)) throw new Error('Upstream mascot markers changed');
adapted = adapted.slice(0, creatureFrom) + fs.readFileSync(path.join(base, 'mascot.ts'), 'utf8') + '\n' + adapted.slice(creatureEnd);
adapted = adapted.replace(palette, "o: '#10a37f', ol: '#a7e2d1', eye: '#073d35'");
adapted = adapted.replace("const MINI = ['.OOOO.', 'OEOOEO', 'OOOOOO', '.OOOO.', '.O..O.']", "const MINI = ['.WWWW.', 'WBEEBW', '.WGGW.', '.W..W.', '.K..K.']")
  .replace('sprite(MINI, { O: C.o, E: C.eye })', "sprite(MINI, { W:'#f7faf8', B:'#152e28', E:'#a7e2d1', G:'#10a37f', K:'#152e28' })");
adapted += '\nSCENES.think = scChatThink;\n';
const renderer = stripTypeScriptTypes(adapted, { mode: 'strip' });
const wrapper = `\nwindow.PixelArtwork = {
  kinds: Object.keys(SCENES),
  mascot(mood = 'awake') { season = null; return critter(4, 4, mood); },
  portrait(kind) {
    season = null;
    const mood = kind==='sleep'?'asleep':kind==='read'?'reading':kind==='bug'||kind==='fire'?'panic':kind==='party'?'hop':['code','shell','build','python','test'].includes(kind)?'typing':'awake';
    return '<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" shape-rendering="crispEdges">'+critter(4,6,mood)+'</svg>';
  },
  render(state) {
    lang = 'zh'; season = festival(new Date());
    const W = 216, L = Math.floor((W - STAGE) / 2);
    return picture(state.kind || 'idle', { W, L, R: W - STAGE - L }, state.crates || [], state.helpers || []);
  }
};`;
fs.writeFileSync(path.join(base, 'artwork.js'), '// Derived from Pixel at Work by Zhuoxing Zhang. MIT; see upstream/LICENSE.\n' + renderer + wrapper);
const context = { window: {}, Date };
vm.runInNewContext(renderer + wrapper, context);
for (const kind of context.window.PixelArtwork.kinds) {
  const svg = context.window.PixelArtwork.render({ kind, crates: [{ kind:'read', ok:true }, { kind:'test', ok:false }], helpers:['read'] });
  if (!svg.startsWith('<svg') || !svg.includes('</svg>') || /undefined|NaN/.test(svg)) throw new Error('Invalid scene: ' + kind);
}
for (const kind of context.window.PixelArtwork.kinds) {
  const svg = context.window.PixelArtwork.portrait(kind);
  if (!svg.startsWith('<svg') || /undefined|NaN/.test(svg)) throw new Error('Invalid portrait: '+kind);
}
console.log(`Verified ${context.window.PixelArtwork.kinds.length} scenes and portraits with the original robot; wrote artwork.js`);

import fs from 'node:fs';
import path from 'node:path';
import vm from 'node:vm';
const base = process.argv[2] || process.cwd();
const context = { window:{}, Date };
vm.runInNewContext(fs.readFileSync(path.join(base,'artwork.js'),'utf8'),context);
const art = context.window.PixelArtwork;
const dir = path.join(base,'docs'); fs.mkdirSync(dir,{recursive:true});
const portrait = mood => '<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" width="128" height="128" shape-rendering="crispEdges">'+art.mascot(mood)+'</svg>';
fs.writeFileSync(path.join(dir,'mascot.svg'),portrait('awake'));
const poses = [['awake','眨眼'],['typing','打字'],['asleep','休息']];
const kinds = [['think','消息气泡 · 动态输入点'],['read','阅读 · 翻书'],['code','编辑 · 写代码'],['idle','等待 · 喝咖啡']];
const html = [
  '<!doctype html><html lang="zh-CN"><meta charset="utf-8"><title>Pixel at Work — scene preview</title>',
  '<style>*{box-sizing:border-box}body{margin:0;padding:26px 32px;background:#17251f;color:#f7faf8;font-family:-apple-system,BlinkMacSystemFont,"Segoe UI",sans-serif}h1{font-size:23px;line-height:30px;margin:0 0 3px}p{font-size:12px;color:#bfd6ca;margin:0}.poses{display:flex;gap:50px;align-items:center;justify-content:center;margin:7px 0 16px}.poses figure{width:128px;text-align:center;margin:0}.poses svg{display:block;width:128px;height:128px}figcaption{font-size:12px;line-height:18px;color:#d8eade}.scenes{display:grid;grid-template-columns:1fr 1fr;gap:18px 30px}.scenes figure{margin:0;padding-top:10px;border-top:1px solid #365547}.scenes svg{display:block;width:100%;height:auto}.scenes figcaption{margin-bottom:6px}.foot{margin-top:16px;font-size:11px;color:#bfd6ca}</style>',
  '<h1>Pixel at Work for Codex</h1><p>原创对话机器人 · 白色圆头 / 黑色面屏 / 绿色对话灯</p><div class="poses">',
  ...poses.map(([mood,label])=>'<figure>'+portrait(mood)+'<figcaption>'+label+'</figcaption></figure>'),
  '</div><div class="scenes">',
  ...kinds.map(([kind,label])=>'<figure><figcaption>'+label+'</figcaption>'+art.render({kind,crates:[],helpers:[]})+'</figure>'),
  '</div><p class="foot">Scene animation adapted from Pixel at Work by Zhuoxing Zhang · MIT · Independent community project</p></html>'
].join('');
fs.writeFileSync(path.join(dir,'preview.html'),html);
console.log('Created original mascot SVG and demonstration page');

import fs from 'node:fs';
import path from 'node:path';
import { createHash } from 'node:crypto';
const base = process.argv[2] || process.cwd();
const version = '1.0.3537.50';
const expected = '5ea526bbd728adda0da4d31219267e96460494a427e4894c4e09d9f320f4b9aa';
const packagePath = path.join(base,'webview2.nupkg');
let bytes;
if(fs.existsSync(packagePath)) bytes = fs.readFileSync(packagePath);
else {
  const url = 'https://api.nuget.org/v3-flatcontainer/microsoft.web.webview2/'+version+'/microsoft.web.webview2.'+version+'.nupkg';
  const response = await fetch(url,{ signal:AbortSignal.timeout(45000) });
  if(!response.ok) throw new Error('WebView2 download failed: HTTP '+response.status);
  bytes = Buffer.from(await response.arrayBuffer());
}
if(createHash('sha256').update(bytes).digest('hex') !== expected) throw new Error('WebView2 SDK checksum mismatch');
if(!fs.existsSync(packagePath)) fs.writeFileSync(packagePath,bytes);
console.log('Verified pinned WebView2 SDK package '+version);

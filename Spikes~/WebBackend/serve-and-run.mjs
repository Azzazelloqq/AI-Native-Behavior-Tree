import http from 'node:http';
import fs from 'node:fs';
import path from 'node:path';
import { spawn } from 'node:child_process';
import os from 'node:os';

const buildRoot = path.resolve(process.argv[2]);
const outputPath = path.resolve(process.argv[3]);
const chromePath = process.argv[4];
const firefoxPath = process.argv[5];
const port = Number(process.argv[6] || 8127);
const results = [];
const children = [];
const browserLog = fs.openSync(outputPath + '.browser.log', 'w');
const pageLogs = [];
const accessLog = fs.openSync(outputPath + '.access.log', 'w');
const mime = new Map([
  ['.html', 'text/html; charset=utf-8'], ['.js', 'text/javascript'], ['.wasm', 'application/wasm'],
  ['.json', 'application/json'], ['.data', 'application/octet-stream'], ['.css', 'text/css'],
  ['.png', 'image/png'], ['.ico', 'image/x-icon'],
]);

const server = http.createServer((request, response) => {
  fs.writeSync(accessLog, `${new Date().toISOString()} ${request.method} ${request.url}\n`);
  if (request.method === 'POST' && request.url === '/__aibt_result') {
    let body = '';
    request.on('data', chunk => body += chunk);
    request.on('end', () => {
      try {
        results.push(JSON.parse(body));
        fs.mkdirSync(path.dirname(outputPath), { recursive: true });
        fs.writeFileSync(outputPath + '.partial', JSON.stringify({ results }, null, 2));
        response.writeHead(204).end();
      }
      catch { response.writeHead(400).end(); }
    });
    return;
  }
  if (request.method === 'POST' && request.url === '/__aibt_log') {
    let body = '';
    request.on('data', chunk => body += chunk);
    request.on('end', () => {
      pageLogs.push(body);
      fs.writeFileSync(outputPath + '.page.log', pageLogs.join('\n'));
      response.writeHead(204).end();
    });
    return;
  }
  const requestedPath = new URL(request.url, `http://127.0.0.1:${port}`).pathname;
  const pathname = requestedPath === '/' ? '/index.html' : requestedPath;
  const file = path.resolve(buildRoot, '.' + pathname);
  if (!file.startsWith(buildRoot + path.sep) || !fs.existsSync(file) || !fs.statSync(file).isFile()) {
    response.writeHead(404).end(); return;
  }
  response.setHeader('Content-Type', mime.get(path.extname(file)) || 'application/octet-stream');
  if (pathname === '/index.html') {
    const hook = `<script>
      const aibtLog = value => fetch('/__aibt_log', {method:'POST', body:String(value)}).catch(()=>{});
      window.addEventListener('error', event => aibtLog('error: ' + event.message + ' @ ' + event.filename + ':' + event.lineno));
      window.addEventListener('unhandledrejection', event => aibtLog('rejection: ' + event.reason));
      const originalError = console.error; console.error = (...args) => { aibtLog('console.error: ' + args.join(' ')); originalError(...args); };
    </script>`;
    response.end(fs.readFileSync(file, 'utf8').replace('</head>', hook + '</head>'));
  } else {
    fs.createReadStream(file).pipe(response);
  }
});

server.listen(port, '127.0.0.1', () => {
  const chromeProfile = fs.mkdtempSync(path.join(os.tmpdir(), 'aibt-chrome-'));
  const firefoxProfile = fs.mkdtempSync(path.join(os.tmpdir(), 'aibt-firefox-'));
  children.push(spawn(chromePath, [
    '--headless=new', '--enable-unsafe-swiftshader', '--use-angle=swiftshader',
    '--enable-logging=stderr', '--no-first-run', '--no-default-browser-check',
    `--user-data-dir=${chromeProfile}`, `http://127.0.0.1:${port}/?browser=chrome`
  ], { stdio: ['ignore', browserLog, browserLog] }));
  children.push(spawn(firefoxPath, [
    '-headless', '-no-remote', '-profile', firefoxProfile,
    `http://127.0.0.1:${port}/?browser=firefox`
  ], { stdio: ['ignore', browserLog, browserLog] }));
});

const deadline = Date.now() + 180000;
while (results.length < 2 && Date.now() < deadline) await new Promise(resolve => setTimeout(resolve, 250));
for (const child of children) { try { child.kill(); } catch {} }
server.close();
fs.closeSync(browserLog);
fs.closeSync(accessLog);
fs.mkdirSync(path.dirname(outputPath), { recursive: true });
fs.writeFileSync(outputPath, JSON.stringify({ observedAt: new Date().toISOString(), results, pageLogs }, null, 2));
if (results.length !== 2 || results.some(result => !result.success)) process.exitCode = 1;

// 檢舉管理小工具(只給開發者用,零依賴)。金鑰:~\.claude\secrets\firebase-admin.json
//   node Tools\reports-admin.js list            列出所有檢舉(依被檢舉 UID 彙總)
//   node Tools\reports-admin.js hide <UID>      隱藏該玩家頭貼(hidden=true)
//   node Tools\reports-admin.js unhide <UID>    取消隱藏
//   node Tools\reports-admin.js clear <UID>     刪掉該玩家被檢舉的紀錄(處理完)
const fs = require('fs'), crypto = require('crypto'), https = require('https'), os = require('os'), path = require('path');
const key = JSON.parse(fs.readFileSync(path.join(os.homedir(), '.claude', 'secrets', 'firebase-admin.json'), 'utf8'));
const b64 = b => Buffer.from(b).toString('base64url');
function req(method, url, headers, body) {
  return new Promise((res, rej) => {
    const r = https.request(url, { method, headers }, s => { let d = ''; s.on('data', c => d += c); s.on('end', () => res({ code: s.statusCode, body: d })); });
    r.on('error', rej); if (body) r.write(body); r.end();
  });
}
async function token() {
  const now = Math.floor(Date.now() / 1000);
  const h = b64(JSON.stringify({ alg: 'RS256', typ: 'JWT' }));
  const c = b64(JSON.stringify({ iss: key.client_email, scope: 'https://www.googleapis.com/auth/datastore', aud: key.token_uri, iat: now, exp: now + 3000 }));
  const sig = crypto.createSign('RSA-SHA256').update(h + '.' + c).sign(key.private_key, 'base64url');
  const body = 'grant_type=' + encodeURIComponent('urn:ietf:params:oauth:grant-type:jwt-bearer') + '&assertion=' + h + '.' + c + '.' + sig;
  const r = await req('POST', key.token_uri, { 'Content-Type': 'application/x-www-form-urlencoded' }, body);
  if (r.code !== 200) throw new Error('token failed ' + r.code + ' ' + r.body);
  return JSON.parse(r.body).access_token;
}
const DOCS = 'https://firestore.googleapis.com/v1/projects/' + key.project_id + '/databases/(default)/documents';
const sv = f => f && (f.stringValue ?? f.integerValue ?? f.booleanValue ?? '');
(async () => {
  const [cmd, uid] = process.argv.slice(2);
  const t = await token(), H = { Authorization: 'Bearer ' + t, 'Content-Type': 'application/json' };
  if (cmd === 'list') {
    const r = await req('GET', DOCS + '/reports?pageSize=300', H);
    const docs = (JSON.parse(r.body).documents) || [];
    if (!docs.length) return console.log('沒有任何檢舉。');
    const by = {};
    for (const d of docs) { const f = d.fields || {}; const u = sv(f.target); (by[u] = by[u] || []).push({ reason: sv(f.reason), from: sv(f.from), ts: Number(sv(f.ts)) }); }
    for (const [u, a] of Object.entries(by).sort((x, y) => y[1].length - x[1].length)) {
      const p = await req('GET', DOCS + '/users/' + u, H); const pf = p.code === 200 ? JSON.parse(p.body).fields : {};
      console.log(`UID ${u}  暱稱「${sv(pf.name) || '?'}」  被檢舉 ${a.length} 次  hidden=${sv(pf.hidden)}`);
      for (const x of a) console.log(`   - ${new Date(x.ts * 1000).toLocaleString()} 來自 ${x.from || '?'}:${x.reason}`);
    }
  } else if ((cmd === 'hide' || cmd === 'unhide') && uid) {
    const r = await req('PATCH', DOCS + '/users/' + uid + '?updateMask.fieldPaths=hidden', H, JSON.stringify({ fields: { hidden: { booleanValue: cmd === 'hide' } } }));
    console.log(r.code === 200 ? `${cmd} ${uid} 完成` : `失敗 ${r.code} ${r.body}`);
  } else if (cmd === 'clear' && uid) {
    const r = await req('GET', DOCS + '/reports?pageSize=300', H); let n = 0;
    for (const d of (JSON.parse(r.body).documents) || []) if (sv((d.fields || {}).target) === uid) { await req('DELETE', 'https://firestore.googleapis.com/v1/' + d.name, H); n++; }
    console.log(`已刪除 ${n} 筆`);
  } else console.log('用法:node Tools\\reports-admin.js list | hide <UID> | unhide <UID> | clear <UID>');
})().catch(e => { console.error(e.message); process.exit(1); });

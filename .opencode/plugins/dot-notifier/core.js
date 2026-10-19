import * as fs from 'node:fs';
import path from 'node:path';
import { randomUUID, createHash } from 'node:crypto';

export const PREFIX = '[OpenCode 自动通知，非用户指令]';
export const ALIAS = 'CallmeCatgirl-memory-upgrade';
const TERMINAL = new Map([
  ['session.execution.succeeded', 'idle'],
  ['session.execution.failed', 'error'],
  ['session.execution.interrupted', 'cancelled'],
]);
const eligible = new Set(['pending', 'claimed']);
const idOK = (v) => typeof v === 'string' && /^[a-zA-Z0-9_-]{1,140}$/.test(v);
export const samePath = (a, b) => typeof a === 'string' && typeof b === 'string'
  && path.resolve(a).toLowerCase() === path.resolve(b).toLowerCase();
const sleep = (ms) => new Promise(resolve => setTimeout(resolve, ms));

// Both producer and sender use the same short transaction lock. Dead PID locks
// may be removed; live/unknown owners are never displaced by a time threshold.
export async function lock(file) {
  fs.mkdirSync(path.dirname(file), { recursive: true });
  const owner = { pid: process.pid, token: randomUUID() };
  for (let n = 0; n < 100; n++) {
    try {
      const fd = fs.openSync(file, 'wx');
      try { fs.writeFileSync(fd, JSON.stringify(owner)); fs.fsyncSync(fd); }
      finally { fs.closeSync(fd); }
      return () => {
        if (JSON.parse(fs.readFileSync(file, 'utf8')).token === owner.token) fs.unlinkSync(file);
      };
    } catch (error) {
      if (error.code !== 'EEXIST') throw error;
      try {
        const old = JSON.parse(fs.readFileSync(file, 'utf8'));
        try { process.kill(old.pid, 0); }
        catch (e) { if (e.code === 'ESRCH') fs.unlinkSync(file); }
      } catch { /* An incomplete/live lock fails closed. */ }
      await sleep(20);
    }
  }
  throw new Error('notification-store-locked');
}

export class Store {
  constructor(directory) { this.directory = directory; this.file = path.join(directory, 'state.json'); }
  read() {
    if (!fs.existsSync(this.file)) return { version: 1, seen: [], runs: {}, queue: [] };
    const s = JSON.parse(fs.readFileSync(this.file, 'utf8'));
    if (s.version !== 1 || !Array.isArray(s.queue) || !Array.isArray(s.seen) || !s.runs) throw new Error('invalid-notification-state');
    return s;
  }
  async change(fn) {
    const unlock = await lock(path.join(this.directory, 'state.lock'));
    try {
      const s = this.read();
      const result = fn(s);
      const tmp = this.file + '.' + randomUUID() + '.tmp';
      const fd = fs.openSync(tmp, 'wx');
      try { fs.writeFileSync(fd, JSON.stringify(s, null, 2)); fs.fsyncSync(fd); }
      finally { fs.closeSync(fd); }
      fs.renameSync(tmp, this.file);
      return result;
    } finally { unlock(); }
  }
}

function cancelIdle(s, sessionID) {
  for (const q of s.queue) if (q.sessionID === sessionID && q.status === 'idle' && eligible.has(q.delivery)) q.delivery = 'cancelled';
}
function outcome(status, run) {
  if (status === 'error') return '本轮执行报错；详细原因请在 OpenCode 查看。';
  if (status === 'cancelled') return '本轮被用户取消；验收未确认。';
  return `本轮生成结束；工具成功 ${run.toolsOK} 次、失败 ${run.toolsFailed} 次；验收未确认。`;
}
export function formatNotice(q) {
  return `${PREFIX}\n项目：${ALIAS}\n通知：${q.id}｜run：${q.runID}｜event：${q.eventID}\n时间：${q.timestamp}\n状态：${q.status}\n结果：${q.outcome}`;
}

export function createObserver({ store, projectRoot, sessionGet, startedAt = Date.now() }) {
  const live = new Map();
  const suppressed = new Set();
  return async function observe(event) {
    if (!event || !idOK(event.id) || !Number.isFinite(event.created) || event.created < startedAt) return;
    const d = event.data;
    if (!d || !idOK(d.sessionID)) return;
    if (event.location && !samePath(event.location.directory, projectRoot)) return;
    const relevant = TERMINAL.has(event.type) || ['session.execution.started', 'session.tool.success', 'session.tool.failed', 'session.deleted', 'session.inbox.enqueued'].includes(event.type);
    if (!relevant) return;
    // No history/context/title is fetched. Unknown ownership fails closed.
    let info;
    try { const r = await sessionGet(d.sessionID); info = r?.data ?? r; } catch { return; }
    if (!info || info.id !== d.sessionID || info.parentID || !samePath(info.location?.directory, projectRoot)) return;
    if (event.type === 'session.inbox.enqueued') {
      if (d.item?.type === 'user') {
        if (JSON.stringify(d.item).includes(PREFIX)) suppressed.add(d.sessionID);
        else suppressed.delete(d.sessionID);
      }
      return;
    }
    if (event.type === 'session.execution.started') live.set(d.sessionID, event.id);
    const runID = live.get(d.sessionID);
    if (!runID) return; // Never announce a historical or already-running turn.
    await store.change(s => {
      if (s.seen.includes(event.id)) return;
      s.seen.push(event.id);
      if (s.seen.length > 4096) s.seen.splice(0, s.seen.length - 4096);
      if (event.type === 'session.execution.started') {
        cancelIdle(s, d.sessionID);
        s.runs[d.sessionID] = { id: runID, toolsOK: 0, toolsFailed: 0, terminal: false };
        return;
      }
      const run = s.runs[d.sessionID];
      if (!run || run.id !== runID) return;
      if (event.type === 'session.tool.success') { if (!run.terminal) run.toolsOK++; return; }
      if (event.type === 'session.tool.failed') { if (!run.terminal) run.toolsFailed++; return; }
      if (event.type === 'session.deleted' || (event.type === 'session.execution.interrupted' && d.reason !== 'user')) {
        cancelIdle(s, d.sessionID); run.terminal = true; return;
      }
      const status = TERMINAL.get(event.type);
      if (!status || suppressed.has(d.sessionID)) return;
      if (run.terminal && status === 'idle') return;
      if (status !== 'idle') cancelIdle(s, d.sessionID);
      run.terminal = true;
      const id = 'ocn_' + createHash('sha256').update(`${ALIAS}:${d.sessionID}:${runID}:${status}`).digest('hex').slice(0, 20);
      if (s.queue.some(q => q.id === id)) return;
      const q = { id, sessionID: d.sessionID, runID, eventID: event.id, timestamp: new Date(event.created).toISOString(), status, outcome: outcome(status, run), delivery: 'pending', created: event.created };
      q.text = formatNotice(q);
      s.queue.push(q);
    });
  };
}

export async function claimNext(store, now = Date.now()) {
  return store.change(s => {
    // A crash after input is never retried automatically. Claims are also held
    // for review rather than silently reclaimed, even when the sender died.
    for (const q of s.queue) if (q.delivery === 'sending') q.delivery = 'uncertain';
    const priority = { error: 0, cancelled: 1, idle: 2 };
    for (const q of s.queue) if (q.delivery === 'pending' && now - q.created > 600_000) q.delivery = 'expired';
    const q = s.queue.filter(q => q.delivery === 'pending').sort((a,b) => priority[a.status] - priority[b.status] || a.created-b.created)[0];
    if (!q) return null;
    q.delivery = 'claimed'; q.claim = randomUUID();
    return { ...q };
  });
}
export async function deliveryState(store, q, state, receipt) {
  return store.change(s => {
    const current = s.queue.find(item => item.id === q.id && item.claim === q.claim);
    if (!current) throw new Error('claim-not-found');
    if (current.delivery === 'cancelled') return false;
    if (state === 'sent' && (current.delivery !== 'sending' || receipt?.text !== current.text || receipt?.id !== current.id || receipt?.targetVerified !== true)) throw new Error('readback-required');
    if (state === 'sending' && current.delivery !== 'claimed') throw new Error('not-claimed');
    current.delivery = state;
    if (state === 'sent') current.deliveredAt = new Date().toISOString();
    return true;
  });
}
export async function drainOnce(store, adapter) {
  if (!adapter) return { status: 'blocked', reason: 'supported-receiver-unavailable' };
  const unlock = await lock(path.join(store.directory, 'sender.lock'));
  try {
    const q = await claimNext(store);
    if (!q) return { status: 'empty' };
    let gate;
    try { gate = await adapter.preflight(q); }
    catch { await deliveryState(store, q, 'pending'); return { status: 'deferred' }; }
    if (!gate?.targetVerified || !gate.emptyComposer || !gate.userIdle) {
      await deliveryState(store, q, 'pending'); return { status: 'deferred' };
    }
    if (!await deliveryState(store, q, 'sending')) return { status: 'cancelled' };
    try {
      const receipt = await adapter.sendAndReadBack(q);
      await deliveryState(store, q, 'sent', receipt);
      return { status: 'sent', id: q.id };
    } catch {
      await deliveryState(store, q, 'uncertain');
      return { status: 'uncertain', id: q.id };
    }
  } finally { unlock(); }
}

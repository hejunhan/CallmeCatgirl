import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { Store, createObserver, samePath } from './core.js';

// Exact project only. Copying this plugin elsewhere does not expand monitoring.
const allowed = 'D:/Unity/CallmeCatgirl';
const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../../..');
export default {
  id: 'callmecatgirl-dot-notifier',
  async setup(ctx) {
    if (!samePath(root, allowed) || !samePath(ctx.location.directory, allowed)) return;
    const store = new Store(path.join(root, '.opencode/dot-notifier'));
    const startedAt = Date.now();
    await store.change(s => { s.plugin = { version: '0.1.0', pid: process.pid, startedAt, adapter: 'manual-desktop-review' }; });
    const observe = createObserver({ store, projectRoot: root, sessionGet: id => ctx.session.get({ sessionID: id }), startedAt });
    const controller = new AbortController();
    const task = (async () => {
      try {
        for await (const e of ctx.event.subscribe({ signal: controller.signal })) {
          try { await observe(e); }
          catch { await store.change(s => { s.health = { state: 'event-error', at: Date.now() }; }).catch(() => {}); }
        }
      } catch {
        if (!controller.signal.aborted) await store.change(s => { s.health = { state: 'subscription-ended', at: Date.now() }; }).catch(() => {});
      }
    })();
    return async () => { controller.abort(); await task; };
  },
};

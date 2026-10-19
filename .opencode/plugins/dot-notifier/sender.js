import { Store, claimNext, deliveryState, drainOnce, lock } from './core.js';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import * as fs from 'node:fs';
const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../../..');
const store = new Store(path.join(root, '.opencode/dot-notifier'));
const [command = 'status', arg] = process.argv.slice(2);
if (command === 'status') {
  console.log(JSON.stringify(store.read().queue.map(({id,status,delivery,timestamp}) => ({id,status,delivery,timestamp})),null,2));
} else if (command === 'drain') {
  console.log(JSON.stringify(await drainOnce(store)));
} else if (command === 'claim') {
  const unlock = await lock(path.join(store.directory, 'sender.lock'));
  try {
    if (store.read().queue.some(q => ['claimed','sending','uncertain'].includes(q.delivery))) throw new Error('previous-delivery-needs-review');
    console.log(JSON.stringify(await claimNext(store),null,2));
  } finally { unlock(); }
} else if (['begin','uncertain','confirm'].includes(command)) {
  const unlock = await lock(path.join(store.directory, 'sender.lock'));
  try {
    const q = store.read().queue.find(q => q.id === arg);
    if (!q?.claim) throw new Error('claim-not-found');
    const receipt = command === 'confirm' ? JSON.parse(fs.readFileSync(process.argv[4], 'utf8')) : undefined;
    console.log(JSON.stringify({updated:await deliveryState(store,q,command==='begin'?'sending':command==='confirm'?'sent':'uncertain',receipt)}));
  } finally { unlock(); }
} else throw new Error('usage: sender.js status|drain|claim|begin ID|uncertain ID|confirm ID receipt.json');

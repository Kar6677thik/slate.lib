import { openDB } from "idb";
export interface Draft {
  key: string;
  id: string;
  path: string;
  baseRevision: string;
  baseSource: string;
  source: string;
  updatedAt: string;
}
const openDatabase = () =>
  openDB("slate-web-drafts", 1, {
    upgrade(db) {
      db.createObjectStore("drafts", { keyPath: "key" });
      db.createObjectStore("captures", { keyPath: "key" });
    },
  });
let opened: ReturnType<typeof openDatabase> | undefined;
const database = () => (opened ??= openDatabase());
const queues = new Map<string, Promise<unknown>>();
function serial<T>(key: string, fn: () => Promise<T>) {
  const task = (queues.get(key) ?? Promise.resolve()).catch(() => {}).then(fn);
  queues.set(key, task);
  void task
    .finally(() => {
      if (queues.get(key) === task) queues.delete(key);
    })
    .catch(() => {});
  return task;
}
export const drafts = {
  get: async (key: string) => {
    await queues.get(key);
    return (await database()).get("drafts", key) as Promise<Draft | undefined>;
  },
  put: (draft: Draft) =>
    serial(draft.key, async () => {
      await (await database()).put("drafts", draft);
    }),
  remove: (key: string) =>
    serial(key, async () => {
      await (await database()).delete("drafts", key);
    }),
  count: async () => (await database()).count("drafts"),
};
export interface CaptureDraft {
  key: string;
  id: string;
  content: string;
  title: string;
  capturedAt: string;
  submitted: boolean;
}
export const captures = {
  get: async (key: string) => {
    await queues.get(key);
    return (await database()).get("captures", key) as Promise<
      CaptureDraft | undefined
    >;
  },
  put: (d: CaptureDraft) =>
    serial(d.key, async () => {
      await (await database()).put("captures", d);
    }),
  remove: (key: string) =>
    serial(key, async () => {
      await (await database()).delete("captures", key);
    }),
};

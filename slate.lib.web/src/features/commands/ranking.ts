export function fuzzyScore(query: string, values: readonly string[]) {
  const needle = query.trim().toLocaleLowerCase();
  if (!needle) return 1;
  let best = 0;
  for (const raw of values) {
    const value = raw.toLocaleLowerCase();
    if (value === needle) best = Math.max(best, 120);
    else if (value.startsWith(needle)) best = Math.max(best, 100 - value.length / 1000);
    else if (value.split(/[^a-z0-9]+/).some((word) => word.startsWith(needle)))
      best = Math.max(best, 82 - value.length / 1000);
    else if (value.includes(needle)) best = Math.max(best, 68 - value.indexOf(needle) / 100);
    else {
      let cursor = 0;
      let gap = 0;
      for (let index = 0; index < value.length && cursor < needle.length; index++) {
        if (value[index] === needle[cursor]) cursor++;
        else if (cursor > 0) gap++;
      }
      if (cursor === needle.length) best = Math.max(best, Math.max(20, 48 - gap));
    }
  }
  return best;
}

export function rankWithRecency(score: number, recentIndex: number) {
  return score + (recentIndex < 0 ? 0 : Math.max(2, 12 - recentIndex / 2));
}

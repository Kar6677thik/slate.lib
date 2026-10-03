// Only loaded by the desktop reader. Note content cannot inject scripts or this URI scheme.
document.addEventListener('keydown', event => {
  if (!event.ctrlKey || event.altKey || event.repeat) return;
  const key = event.key.toLowerCase();
  const commands = { k: 'search', p: 'commands', n: event.shiftKey ? 'folder' : 'note',
    s: 'save', w: 'close', e: 'mode', tab: event.shiftKey ? 'previous-tab' : 'next-tab' };
  const command = key === 'f' && event.shiftKey ? 'search' : commands[key];
  if (!command) return;
  event.preventDefault(); event.stopPropagation();
  location.href = 'slate-command://' + command;
}, true);

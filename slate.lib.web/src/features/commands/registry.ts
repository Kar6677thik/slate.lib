import type { CommandDefinition, CommandResult } from "./types";
import { fuzzyScore, rankWithRecency } from "./ranking";

export interface CommandSource {
  id: string;
  collect: (query: string) => CommandResult[];
}

export function availableCommands(commands: readonly CommandDefinition[]) {
  return commands.filter(
    (command) => command.mobileVisible !== false && (command.when?.() ?? true),
  );
}

export function commandResults(
  commands: readonly CommandDefinition[],
  query: string,
  recentIds: readonly string[],
) {
  return availableCommands(commands).flatMap<CommandResult>((command) => {
    const score = fuzzyScore(query, [
      command.title,
      command.description ?? "",
      ...command.keywords,
    ]);
    if (query && !score) return [];
    return [{
      key: `command:${command.id}`,
      commandId: command.id,
      title: command.title,
      description: command.description,
      icon: command.icon,
      group: command.group,
      shortcut: command.shortcut,
      dangerous: command.dangerous,
      score: rankWithRecency(query ? score : 24, recentIds.indexOf(command.id)),
      execute: command.execute,
    }];
  });
}

export function collectCommandSources(
  sources: readonly CommandSource[],
  query: string,
) {
  return sources.flatMap((source) => source.collect(query));
}

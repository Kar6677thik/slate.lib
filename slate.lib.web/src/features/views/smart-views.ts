import {
  CircleHelp,
  Clock3,
  Unlink,
  Workflow,
  SquareCode,
  GraduationCap,
  ListChecks,
  type LucideIcon,
} from "lucide-react";

export const SMART_VIEWS = [
  {
    id: "unanswered",
    title: "Unanswered questions",
    description: "Questions that are still open or have no status.",
    icon: CircleHelp,
  },
  {
    id: "modified",
    title: "Recently modified",
    description: "Notes ordered by their known modified or created date.",
    icon: Clock3,
  },
  {
    id: "orphans",
    title: "Orphan notes",
    description: "Notes with no incoming links from another note.",
    icon: Unlink,
  },
  {
    id: "diagrams",
    title: "Diagrams",
    description: "Notes containing a Mermaid diagram.",
    icon: Workflow,
  },
  {
    id: "code",
    title: "Code notes",
    description: "Notes containing one or more fenced code blocks.",
    icon: SquareCode,
  },
  {
    id: "learning",
    title: "Currently learning",
    description: "Material marked as learning or in progress.",
    icon: GraduationCap,
  },
  {
    id: "review",
    title: "Needs review",
    description: "Notes explicitly marked for review.",
    icon: ListChecks,
  },
] as const satisfies readonly {
  id: string;
  title: string;
  description: string;
  icon: LucideIcon;
}[];

export type SmartViewId = (typeof SMART_VIEWS)[number]["id"];
export function isSmartView(value: string | null): value is SmartViewId {
  return SMART_VIEWS.some((view) => view.id === value);
}
export function smartView(value: string) {
  return SMART_VIEWS.find((view) => view.id === value);
}

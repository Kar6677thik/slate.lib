import { describe, expect, it } from "vitest";
import { plainExcerpt } from "@/lib/markdown/plain";

describe("plain excerpts", () => {
  it("drops badges and images and keeps link labels", () => {
    const source = "# Flux\n\n[![CI](https://x/badge.svg)](https://x/ci) A **multi-tenant** gateway built on [ASP.NET Core](https://dot.net) and `YARP`.\n\n![Demo](https://x/demo.svg) <sub>Recorded from [`demo.sh`](https://x)</sub>";
    expect(plainExcerpt(source)).toBe("Flux A multi-tenant gateway built on ASP.NET Core and YARP. Recorded from demo.sh");
  });
  it("renders wiki links by label, strips frontmatter, quotes and task markers", () => {
    expect(plainExcerpt("---\nid: x\n---\n> See [[Redis basics|Redis]] and [[Kafka#Topics]].\n- [x] _done_ item")).toBe("See Redis and Kafka. done item");
  });
  it("keeps code text without fences", () => {
    expect(plainExcerpt("```bash\nkubectl get pods\n```")).toBe("kubectl get pods");
  });
});

---
id: 2b77366d-aa70-4eac-8e46-05a5f4150ee7
title: PostgreSQL
tags: [databases, learning]
---

# PostgreSQL

PostgreSQL is a relational database. These are small working notes, stored as ordinary **Markdown files**.

## Example query

```sql
SELECT name, created_at
FROM users
ORDER BY created_at DESC
LIMIT 10;
```

## Concepts to explore

- Multi-version concurrency control (MVCC)
- Write-ahead logging (WAL)
- [Indexes](Indexes.md) and query plans

> A useful note explains an idea in your own words and keeps one concrete example.

| Tool | Question it helps answer |
| --- | --- |
| EXPLAIN | Which plan will the query use? |
| EXPLAIN ANALYZE | What happened when it ran? |

See the [PostgreSQL documentation](https://www.postgresql.org/docs/) for reference.

---
id: c1de1f79-6414-4efe-9c3d-f24e8324b6b3
---

# Database indexes

An index trades storage and write work for faster access to selected rows.

1. Start with a real query.
2. Inspect its execution plan.
3. Measure before and after adding an index.

```sql
CREATE INDEX users_created_at_idx ON users (created_at);
```

Return to [PostgreSQL](PostgreSQL.md).

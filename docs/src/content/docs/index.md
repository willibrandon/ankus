---
title: Ankus
description: Write PostgreSQL extensions in C# with .NET Native AOT.
template: splash
hero:
  actions:
    - text: Get started
      link: ./getting-started/functions/
      icon: right-arrow
---

Ankus ports [pgrx](https://github.com/pgcentralfoundation/pgrx) to .NET Native AOT.
Define PostgreSQL functions, aggregates, operators, casts, triggers, custom types,
and configuration settings in C#. Run [background workers](/background-workers/),
[share memory between backends](/shared-memory/), and extend the planner and
executor with [native hooks](/raw-values/#managed-native-callbacks-and-hooks) and
[custom scan providers](/custom-scans/). Access PostgreSQL's native APIs through
[generated bindings](/raw-values/#native-postgresql-declarations). Publish it all
as a native extension.

```csharp
[PgFunction]
public static int Add(int a, int b)
    => checked(a + b);
```

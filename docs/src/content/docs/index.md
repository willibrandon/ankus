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

```csharp
[PgFunction]
public static int Add(int a, int b)
    => checked(a + b);
```

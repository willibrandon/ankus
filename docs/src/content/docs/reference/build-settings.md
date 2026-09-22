---
title: Build settings
description: Configure extension names, versions, PostgreSQL headers, and installation paths.
---

Set extension properties in your project file:

```xml
<PropertyGroup>
  <AnkusExtensionName>hello</AnkusExtensionName>
  <AnkusExtensionVersion>1.0.0</AnkusExtensionVersion>
</PropertyGroup>
```

| Property | Default | Purpose |
| --- | --- | --- |
| `AnkusExtensionName` | Lowercase assembly name, with periods replaced by underscores | Names the control and SQL files |
| `AnkusExtensionVersion` | Project `Version` | Selects the versioned SQL filename and control-file version |
| `AnkusPostgresMajor` | `18` | Selects the server headers used to compile the native wrapper |
| `AnkusPgConfigPath` | Registered or discovered installation | Selects an exact `pg_config`; the tool sets this automatically |

## Installation paths

| Artifact | Standard location |
| --- | --- |
| Native library | `pg_config --pkglibdir` |
| Control and versioned SQL files | `extension/` under `pg_config --sharedir` |

The control file resolves the library through `dynamic_library_path`, normally
`$libdir`. PostgreSQL 18's `extension_control_path` also allows a separate
directory for extension files.

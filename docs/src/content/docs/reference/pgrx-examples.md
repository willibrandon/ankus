---
title: pgrx examples
description: Find the Ankus sample or guide that corresponds to each pgrx example.
---

Each example in pgrx's `pgrx-examples` directory has an Ankus counterpart: a
runnable sample under [`samples/`](https://github.com/willibrandon/ankus/tree/main/samples)
or, for Rust-only mechanisms, a guide that explains the .NET equivalent. Samples
are ordinary projects; publish and install one as described in
[Publish and install](/getting-started/publishing/), then run the SQL in its README.

| pgrx example | Ankus | Notes |
| --- | --- | --- |
| `aggregate` | [`Ankus.Examples.Aggregates`](https://github.com/willibrandon/ankus/tree/main/samples/Ankus.Examples.Aggregates) | Managed state, parallel transport and moving windows. See [Aggregates](/aggregates/). |
| `arrays` | [`Ankus.Examples.Arrays`](https://github.com/willibrandon/ankus/tree/main/samples/Ankus.Examples.Arrays) | Borrowed and copied arrays. See [Arrays](/arrays/). |
| `bgworker` | [`Ankus.Examples.BackgroundWorkers`](https://github.com/willibrandon/ankus/tree/main/samples/Ankus.Examples.BackgroundWorkers) | See [Background workers](/background-workers/). |
| `benching` | [`Ankus.Examples.Benchmarks`](https://github.com/willibrandon/ankus/tree/main/samples/Ankus.Examples.Benchmarks) | In-backend benchmarks through `ankus bench`. See [Benchmarks](/benchmarks/). |
| `bytea` | [`Ankus.Examples.Bytea`](https://github.com/willibrandon/ankus/tree/main/samples/Ankus.Examples.Bytea) | |
| `composite_type` | [`Ankus.Examples.Composites`](https://github.com/willibrandon/ankus/tree/main/samples/Ankus.Examples.Composites) | See [Composite values](/composites/). |
| `custom_types` | [`Ankus.Examples.CustomTypes`](https://github.com/willibrandon/ankus/tree/main/samples/Ankus.Examples.CustomTypes) | See [Custom types](/custom-types/). |
| `datetime` | [`Ankus.Examples.DateTime`](https://github.com/willibrandon/ankus/tree/main/samples/Ankus.Examples.DateTime) | See [Date and time values](/date-and-time/). |
| `errors` | [`Ankus.Examples.Errors`](https://github.com/willibrandon/ankus/tree/main/samples/Ankus.Examples.Errors) | Managed exceptions replace Rust panics. See [Logging and errors](/logging/). |
| `json` | [`Ankus.Examples.Json`](https://github.com/willibrandon/ankus/tree/main/samples/Ankus.Examples.Json) | See [JSON and UUID values](/json-and-uuid/). |
| `numeric` | [`Ankus.Examples.Numeric`](https://github.com/willibrandon/ankus/tree/main/samples/Ankus.Examples.Numeric) | See [Numeric values](/numeric/). |
| `operators` | [`Ankus.Examples.Operators`](https://github.com/willibrandon/ankus/tree/main/samples/Ankus.Examples.Operators) | See [Operators and casts](/operators-and-casts/). |
| `range` | [`Ankus.Examples.Ranges`](https://github.com/willibrandon/ankus/tree/main/samples/Ankus.Examples.Ranges) | See [Ranges](/ranges/). |
| `spi`, `spi_srf` | [`Ankus.Examples.Spi`](https://github.com/willibrandon/ankus/tree/main/samples/Ankus.Examples.Spi) | See [SPI queries](/spi/). |
| `srf` | [`Ankus.Examples.Sets`](https://github.com/willibrandon/ankus/tree/main/samples/Ankus.Examples.Sets) | C# iterators replace pgrx's iterator types. See [Sets and tables](/sets-and-tables/). |
| `strings` | [`Ankus.Examples.Strings`](https://github.com/willibrandon/ankus/tree/main/samples/Ankus.Examples.Strings) | |
| `triggers` | [`Ankus.Examples.Triggers`](https://github.com/willibrandon/ankus/tree/main/samples/Ankus.Examples.Triggers) | See [Triggers](/triggers/). |

Ankus also includes samples without a direct pgrx counterpart:
[`Ankus.Examples.CustomScans`](https://github.com/willibrandon/ankus/tree/main/samples/Ankus.Examples.CustomScans),
[`Ankus.Examples.Configuration`](https://github.com/willibrandon/ankus/tree/main/samples/Ankus.Examples.Configuration),
[`Ankus.Examples.Enums`](https://github.com/willibrandon/ankus/tree/main/samples/Ankus.Examples.Enums),
[`Ankus.Examples.EventTriggers`](https://github.com/willibrandon/ankus/tree/main/samples/Ankus.Examples.EventTriggers) and
[`Ankus.Examples.Initialization`](https://github.com/willibrandon/ankus/tree/main/samples/Ankus.Examples.Initialization).

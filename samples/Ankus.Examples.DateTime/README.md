# Datetime example

This ports pgrx's datetime example: native interval arithmetic, ISO formatting,
timezone conversion, bounded random values and PostgreSQL's four clocks.
The twenty-one SQL functions use PostgreSQL's full-range temporal types.

The following queries assume the extension is installed in `public`. Qualify
the functions with the schema where you installed `ankus_datetime`.

```sql
SELECT public.add_interval(date '2000-01-31', interval '1 month');
SELECT public.subtract_interval(time '00:00', interval '1 hour');
SELECT public.mul_interval(interval '1 month 2 days', 0.5);
SELECT public.div_interval(interval '1 day', 2.0);
SELECT public.compose_timestamp(date '2000-12-31', time '24:00');
```

`add_interval` and `subtract_interval` each accept `date`, `time`, `timetz`,
`timestamp`, `timestamptz` or `interval`. A date produces a timestamp; the other
overloads retain their input type. PostgreSQL handles month-end adjustment,
midnight wrapping, infinities and microseconds. A `timestamptz` calendar day
can differ from twenty-four elapsed hours across a daylight-saving change.

```sql
SET TIME ZONE 'America/New_York';
SELECT public.to_iso_string(timestamptz '2024-03-10 07:00:00+00');
SELECT public.to_iso_string(timestamptz '2024-03-10 07:00:00+00', 'Asia/Kathmandu');
SELECT public.set_timezone(timestamp '2024-03-10 02:30:00', 'UTC');
```

The one-argument ISO overload uses the session timezone. The explicit overload
formats the instant in the named zone without changing the session. Neither
depends on `DateStyle`. `set_timezone` first interprets its input in the current
session timezone, then returns the destination's local timestamp. It retains
PostgreSQL's behavior for ambiguous or nonexistent daylight-saving wall times.

Session-dependent functions use `PgVolatility.Stable`: one-argument ISO
formatting, `timestamptz` interval arithmetic and `set_timezone`. This corrects
the upstream sample's immutable declarations so prepared queries see timezone
changes. The other arithmetic and explicit-zone ISO overload remain immutable.
These functions are parallel-safe.

```sql
SELECT public.compose_timestamp(public.random_date(), public.random_time())
FROM generate_series(1, 10);

BEGIN;
SELECT pg_sleep(0.01);
SELECT * FROM public.all_times();
ROLLBACK;
```

The random samplers retain upstream's upper-exclusive bounds: years 1978–2022,
months January–November, valid days 1–30, hours 0–22 and minutes/seconds 0–58.
The date sampler selects the valid day interval directly, preserving the same
conditional distribution as upstream's rejection loop. Both are volatile and
parallel-safe; generated times have no fractional seconds.

`all_times` returns one row with `now`, `transaction_timestamp`,
`statement_timestamp` and `clock_timestamp`. The first two are identical and
fixed for the transaction. Statement time changes for each statement; clock
time reads the wall clock when the function executes.

Inputs are strict: a SQL NULL argument returns SQL NULL. Native errors retain
their PostgreSQL SQLSTATE. In managed extension code, use
`PgTransaction.RunInSubtransaction` when you intend to catch a native error and
continue backend work.

See [the upstream notice](ThirdPartyNotices.md) for the original example's MIT
license and attribution.

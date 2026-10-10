// Independent native observations for the test suite, including servers predating the catalog view.
[assembly: PgSql("memory-observation", """
    CREATE SCHEMA ankus_test_memory;
    DO $memory_observation$
    DECLARE
        -- Name the fixture's actual file: it carries its platform's suffix, while macOS servers before 16 imply .so.
        library text := 'Ankus.AllocatorFixture' || CASE
            WHEN version() LIKE '%apple-darwin%' THEN '.dylib'
            WHEN version() LIKE '%Visual C++%' OR version() LIKE '%mingw%' THEN '.dll'
            ELSE '.so'
        END;
    BEGIN
        IF current_setting('server_version_num')::integer < 140000 THEN
            EXECUTE format($create$
                CREATE FUNCTION ankus_test_memory.snapshot()
                    RETURNS TABLE(name text, ident text, total_bytes bigint, total_nblocks bigint,
                        free_bytes bigint, free_chunks bigint, used_bytes bigint)
                    AS %L, 'ankus_test_memory_contexts' LANGUAGE c PARALLEL RESTRICTED
                $create$, library);
            REVOKE ALL ON FUNCTION ankus_test_memory.snapshot() FROM PUBLIC;
            CREATE VIEW ankus_test_memory.contexts AS SELECT * FROM ankus_test_memory.snapshot();
        ELSE
            CREATE VIEW ankus_test_memory.contexts AS
                SELECT name, ident, total_bytes, total_nblocks, free_bytes, free_chunks, used_bytes
                FROM pg_catalog.pg_backend_memory_contexts;
        END IF;
    END
    $memory_observation$;
    """, Requires = ["sql-first"])]

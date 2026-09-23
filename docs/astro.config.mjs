import { defineConfig } from 'astro/config';
import starlight from '@astrojs/starlight';

export default defineConfig({
  outDir: '../artifacts/docs',
  cacheDir: './node_modules/.astro-cache',
  integrations: [
    starlight({
      title: 'Ankus',
      description: 'PostgreSQL extensions in C# with .NET Native AOT.',
      customCss: ['./src/styles/custom.css'],
      expressiveCode: {
        customizeTheme(theme) {
          // The bundled themes omit C# type and variable scopes, including attributes and generics.
          theme.settings.push(
            {
              scope: ['source.cs entity.name.type', 'source.cs support.type', 'source.cs support.class'],
              settings: { foreground: theme.type === 'dark' ? '#7FDBCA' : '#006B60' },
            },
            {
              scope: [
                'source.cs entity.name.variable',
                'source.cs variable',
                'variable.other.readwrite.cs',
                'variable.other.object.cs',
              ],
              settings: { foreground: theme.type === 'dark' ? '#9CDCFE' : '#234A97' },
            },
          );
        },
      },
      sidebar: [
        {
          label: 'Getting started',
          items: [
            { label: 'Write a function', slug: 'getting-started/functions' },
            { label: 'Test an extension', slug: 'getting-started/testing' },
            { label: 'Publish and install', slug: 'getting-started/publishing' },
          ],
        },
        {
          label: 'Working with PostgreSQL',
          items: [
            { label: 'Function declarations', slug: 'function-declarations' },
            { label: 'Extension initialization', slug: 'initialization' },
            { label: 'Configuration settings', slug: 'configuration' },
            { label: 'Sets and tables', slug: 'sets-and-tables' },
            { label: 'Composite values', slug: 'composites' },
            { label: 'Triggers', slug: 'triggers' },
            { label: 'Event triggers', slug: 'event-triggers' },
            { label: 'Aggregates', slug: 'aggregates' },
            { label: 'Operators and casts', slug: 'operators-and-casts' },
            { label: 'Custom SQL', slug: 'custom-sql' },
            { label: 'SPI queries', slug: 'spi' },
            { label: 'Arrays', slug: 'arrays' },
            { label: 'JSON and UUID values', slug: 'json-and-uuid' },
            { label: 'Numeric values', slug: 'numeric' },
            { label: 'Date and time values', slug: 'date-and-time' },
            { label: 'Network values', slug: 'network' },
            { label: 'Geometric values', slug: 'geometry' },
            { label: 'Ranges', slug: 'ranges' },
            { label: 'Enumerated types', slug: 'enums' },
            { label: 'Logging and errors', slug: 'logging' },
          ],
        },
        {
          label: 'Reference',
          items: [
            { label: 'Command-line tool', slug: 'reference/cli' },
            { label: 'Build settings', slug: 'reference/build-settings' },
            { label: 'Execution and lifetime', slug: 'reference/execution' },
          ],
        },
        {
          label: 'API reference',
          items: [
            { label: 'Overview', slug: 'api' },
            { autogenerate: { directory: 'api/generated' } },
          ],
        },
      ],
    }),
  ],
});

import { defineConfig } from 'astro/config';
import { satteri } from '@astrojs/markdown-satteri';
import starlight from '@astrojs/starlight';

const siteBase = '/ankus';

function addSiteBase(url) {
  if (
    !url.startsWith('/') ||
    url.startsWith('//') ||
    url === siteBase ||
    url.startsWith(`${siteBase}/`)
  ) {
    return url;
  }

  return `${siteBase}${url}`;
}

function updateUrl(node, context) {
  const url = addSiteBase(node.url);
  if (url !== node.url) {
    context.setProperty(node, 'url', url);
  }
}

const siteBaseLinks = {
  name: 'ankus-site-base-links',
  link: updateUrl,
  definition: updateUrl,
  image: updateUrl,
};

export default defineConfig({
  site: 'https://willibrandon.github.io/ankus',
  base: siteBase,
  outDir: '../artifacts/docs',
  cacheDir: './node_modules/.astro-cache',
  markdown: {
    processor: satteri({ mdastPlugins: [siteBaseLinks] }),
  },
  integrations: [
    starlight({
      title: 'Ankus',
      description: 'PostgreSQL extensions in C# with .NET Native AOT.',
      disable404Route: true,
      social: [
        {
          icon: 'github',
          label: 'GitHub',
          href: 'https://github.com/willibrandon/ankus',
        },
      ],
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
            { label: 'Calling PostgreSQL functions', slug: 'calling-functions' },
            { label: 'Catalog name lookups', slug: 'catalog-lookups' },
            { label: 'Extension initialization', slug: 'initialization' },
            { label: 'Configuration settings', slug: 'configuration' },
            { label: 'Sets and tables', slug: 'sets-and-tables' },
            { label: 'Composite values', slug: 'composites' },
            { label: 'Custom types', slug: 'custom-types' },
            { label: 'Triggers', slug: 'triggers' },
            { label: 'Event triggers', slug: 'event-triggers' },
            { label: 'Aggregates', slug: 'aggregates' },
            { label: 'Operators and casts', slug: 'operators-and-casts' },
            { label: 'Custom SQL', slug: 'custom-sql' },
            { label: 'SPI queries', slug: 'spi' },
            { label: 'Transaction IDs', slug: 'transaction-ids' },
            { label: 'Tuple locations', slug: 'item-pointers' },
            { label: 'Transaction callbacks', slug: 'transaction-callbacks' },
            { label: 'Memory contexts', slug: 'memory-contexts' },
            { label: 'StringInfo buffers', slug: 'stringinfo' },
            { label: 'PostgreSQL lists', slug: 'lists' },
            { label: 'Arrays', slug: 'arrays' },
            { label: 'Polymorphic values', slug: 'polymorphic-values' },
            { label: 'Raw values and custom types', slug: 'raw-values' },
            { label: 'Internal state', slug: 'internal-state' },
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

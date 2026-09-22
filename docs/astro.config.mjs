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
      sidebar: [
        {
          label: 'Getting started',
          items: [
            { label: 'Write a function', slug: 'getting-started/functions' },
            { label: 'Publish and install', slug: 'getting-started/publishing' },
          ],
        },
        {
          label: 'Working with PostgreSQL',
          items: [
            { label: 'SPI queries', slug: 'spi' },
            { label: 'JSON and UUID values', slug: 'json-and-uuid' },
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

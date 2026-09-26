import csharp from '@shikijs/langs/csharp';
import sql from '@shikijs/langs/sql';

// The upstream C# grammar tries namespace imports before top-level statements.
// Recognize using declarations first, reusing its complete local-variable rule
// for generics, qualified types and initializers. Static imports and aliases
// continue through the upstream directive rules.
const csharpGrammar = structuredClone(csharp[0]);
csharpGrammar.patterns.unshift({
  begin: String.raw`\b(using)\b(?=\s+(?!static\b|unsafe\b)${csharpGrammar.repository['local-variable-declaration'].begin})`,
  beginCaptures: { 1: { name: 'keyword.control.context.using.cs' } },
  end: '(?=;)',
  patterns: [
    { include: '#comment' },
    { include: '#local-variable-declaration' },
  ],
});

// SHOW is a PostgreSQL command missing from the bundled generic SQL grammar.
// A grammar rule preserves the existing handling of strings and comments.
const sqlGrammar = structuredClone(sql[0]);
sqlGrammar.patterns.unshift({
  match: String.raw`(?i:\bshow\b)`,
  name: 'keyword.other.sql',
});

export const documentationGrammars = [csharpGrammar, sqlGrammar];

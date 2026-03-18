#!/usr/bin/env node
/**
 * Generates hub-contracts.generated.ts from C# positional records in
 * server/src/RpgGame.Api/Contracts/Hub/
 *
 * Run via: npm run contracts:hub
 */

const fs = require('fs');
const path = require('path');

const HUB_DIR = path.resolve(__dirname, '../../server/src/RpgGame.Api/Contracts/Hub');
const OUT_FILE = path.resolve(__dirname, '../src/app/contracts/hub-contracts.generated.ts');

// Files that contain service-internal types, not wire payloads
const SKIP_FILES = new Set(['EndTurnResult.cs', 'ServiceResults.cs']);

// C# type → TypeScript type
function mapType(csType) {
  csType = csType.trim();

  const nullable = csType.endsWith('?');
  const base = nullable ? csType.slice(0, -1).trim() : csType;

  let ts = mapBaseType(base);
  return nullable ? `${ts} | null` : ts;
}

function mapBaseType(t) {
  // List<T> → T[]
  const listMatch = t.match(/^List<(.+)>$/);
  if (listMatch) return `${mapType(listMatch[1])}[]`;

  // Dictionary<string, T> → { [key: string]: T }
  const dictMatch = t.match(/^Dictionary<string,\s*(.+)>$/);
  if (dictMatch) return `{ [key: string]: ${mapType(dictMatch[1])} }`;

  switch (t) {
    case 'string':  return 'string';
    case 'int':     return 'number';
    case 'bool':    return 'boolean';
    case 'Guid':    return 'string';
    default:        return t; // pass through — same name for cross-type references
  }
}

// PascalCase → camelCase
function toCamelCase(name) {
  return name.charAt(0).toLowerCase() + name.slice(1);
}

// Extract all `public record Foo(...)` from a file's text.
// Handles both single-line and multi-line parameter lists.
function extractRecords(source) {
  const records = [];

  // Collapse all whitespace runs to a single space to simplify parsing
  const flat = source.replace(/\s+/g, ' ');

  const recordRe = /public record (\w+)\(([^)]*)\)/g;
  let m;
  while ((m = recordRe.exec(flat)) !== null) {
    const name = m[1];
    const rawParams = m[2].trim();
    const properties = [];

    if (rawParams) {
      // Each param: "Type? Name" or "List<T> Name" etc.
      // Split on commas that are NOT inside angle brackets
      const params = splitParams(rawParams);
      for (const param of params) {
        const parts = param.trim().split(/\s+/);
        const propName = parts[parts.length - 1];
        const csType = parts.slice(0, -1).join(' ');
        properties.push({ name: propName, type: csType });
      }
    }

    records.push({ name, properties });
  }

  return records;
}

// Split "Type1 A, List<Foo, Bar> B, Type2 C" on top-level commas only
function splitParams(params) {
  const result = [];
  let depth = 0;
  let current = '';
  for (const ch of params) {
    if (ch === '<') depth++;
    else if (ch === '>') depth--;
    if (ch === ',' && depth === 0) {
      result.push(current.trim());
      current = '';
    } else {
      current += ch;
    }
  }
  if (current.trim()) result.push(current.trim());
  return result;
}

function generateInterface(record) {
  const lines = [`export interface ${record.name} {`];
  for (const prop of record.properties) {
    const tsType = mapType(prop.type);
    const tsName = toCamelCase(prop.name);
    // If tsType contains " | null", use optional-style comment; keep it explicit
    lines.push(`  ${tsName}: ${tsType};`);
  }
  lines.push('}');
  return lines.join('\n');
}

// --- main ---

const csFiles = fs.readdirSync(HUB_DIR)
  .filter(f => f.endsWith('.cs') && !SKIP_FILES.has(f))
  .sort();

const allRecords = [];
for (const file of csFiles) {
  const source = fs.readFileSync(path.join(HUB_DIR, file), 'utf8');
  allRecords.push(...extractRecords(source));
}

if (allRecords.length === 0) {
  console.error('No records found — check HUB_DIR path');
  process.exit(1);
}

const header = [
  '// AUTO-GENERATED — do not edit by hand.',
  '// Source: C# records in server/src/RpgGame.Api/Contracts/Hub/',
  '// Regenerate: npm run contracts:hub',
  '',
].join('\n');

const body = allRecords.map(generateInterface).join('\n\n');

fs.writeFileSync(OUT_FILE, header + body + '\n');

console.log(`Generated ${allRecords.length} interfaces → ${path.relative(process.cwd(), OUT_FILE)}`);
allRecords.forEach(r => console.log(`  ${r.name}`));

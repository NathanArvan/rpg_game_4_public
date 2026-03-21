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
    case 'long':    return 'number';
    case 'float':   return 'number';
    case 'double':  return 'number';
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
      // Each param: "Type? Name" or "List<T> Name" or "Type Name = default" etc.
      // Split on commas that are NOT inside angle brackets
      const params = splitParams(rawParams);
      for (const param of params) {
        // Strip default values: "string Name = value" → "string Name"
        const withoutDefault = param.trim().replace(/\s*=\s*\S+$/, '');
        const parts = withoutDefault.trim().split(/\s+/);
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

// Extract nested `public static class` blocks containing `public const string` fields.
// Uses a brace-counting scan to handle nested classes correctly.
// Returns an array of { outerName, innerClasses: [{ innerName, constants: [{name, value}] }] }.
function extractStaticConstClasses(source) {
  const result = [];

  const outerRe = /public\s+static\s+class\s+(\w+)\s*\{/g;
  let outerMatch;

  while ((outerMatch = outerRe.exec(source)) !== null) {
    const outerName = outerMatch[1];
    // Find the matching closing brace using depth counting
    const outerBody = extractBracedBlock(source, outerMatch.index + outerMatch[0].length - 1);
    if (outerBody === null) continue;

    // Now find inner static classes inside outerBody
    const innerRe = /public\s+static\s+class\s+(\w+)\s*\{/g;
    const innerClasses = [];
    let innerMatch;

    while ((innerMatch = innerRe.exec(outerBody)) !== null) {
      const innerName = innerMatch[1];
      const innerBody = extractBracedBlock(outerBody, innerMatch.index + innerMatch[0].length - 1);
      if (innerBody === null) continue;

      // Match: public const string Name = "value";  OR  = nameof(Name);
      const constRe = /public\s+const\s+string\s+(\w+)\s*=\s*(?:"([^"]+)"|nameof\((\w+)\))\s*;/g;
      const constants = [];
      let constMatch;
      while ((constMatch = constRe.exec(innerBody)) !== null) {
        const name = constMatch[1];
        const value = constMatch[2] !== undefined ? constMatch[2] : constMatch[3];
        constants.push({ name, value });
      }

      if (constants.length > 0) {
        innerClasses.push({ innerName, constants });
      }
    }

    if (innerClasses.length > 0) {
      result.push({ outerName, innerClasses });
      // Advance outerRe past the end of this outer block to avoid re-matching inner classes
      outerRe.lastIndex = outerMatch.index + outerMatch[0].length + outerBody.length + 1;
    }
  }

  return result;
}

// Given a source string and the index of the opening `{`, returns the content
// between that `{` and its matching `}` (exclusive of the braces themselves).
function extractBracedBlock(source, openBraceIndex) {
  if (source[openBraceIndex] !== '{') return null;
  let depth = 0;
  for (let i = openBraceIndex; i < source.length; i++) {
    if (source[i] === '{') depth++;
    else if (source[i] === '}') {
      depth--;
      if (depth === 0) return source.slice(openBraceIndex + 1, i);
    }
  }
  return null; // unmatched
}

function generateConstObject(staticClass) {
  const lines = [`export const ${staticClass.outerName} = {`];
  for (const inner of staticClass.innerClasses) {
    lines.push(`  ${inner.innerName}: {`);
    for (const c of inner.constants) {
      lines.push(`    ${c.name}: '${c.value}',`);
    }
    lines.push(`  },`);
  }
  lines.push(`} as const;`);
  return lines.join('\n');
}

// --- main ---

const csFiles = fs.readdirSync(HUB_DIR)
  .filter(f => f.endsWith('.cs') && !SKIP_FILES.has(f))
  .sort();

const allRecords = [];
const allStaticClasses = [];
for (const file of csFiles) {
  const source = fs.readFileSync(path.join(HUB_DIR, file), 'utf8');
  allRecords.push(...extractRecords(source));
  allStaticClasses.push(...extractStaticConstClasses(source));
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

const interfaces = allRecords.map(generateInterface).join('\n\n');
const constObjects = allStaticClasses.map(generateConstObject).join('\n\n');
const body = constObjects ? interfaces + '\n\n' + constObjects : interfaces;

fs.writeFileSync(OUT_FILE, header + body + '\n');

console.log(`Generated ${allRecords.length} interfaces, ${allStaticClasses.length} const objects → ${path.relative(process.cwd(), OUT_FILE)}`);
allRecords.forEach(r => console.log(`  interface ${r.name}`));
allStaticClasses.forEach(s => console.log(`  const ${s.outerName}`));

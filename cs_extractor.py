#!/usr/bin/env python3
"""
C# Variable and Function Extractor

Scans a folder recursively for .cs files and extracts all variables and
functions, writing results to a CSV file.

CSV columns:
    경로        - full file path including filename
    파일이름    - filename only
    형식        - variable: data type  /  function: return type
    이름        - variable: name  /  function: name(params)
    입력자료형  - function only: parameter types (e.g. "int, string"), "-" if none / n/a
    출력자료형  - function only: return type,  "-" for variables

Usage:
    python cs_extractor.py <folder_path> <output_csv>
    python cs_extractor.py ./MyProject output.csv
"""

import csv
import re
import sys
from pathlib import Path


# ── C# keyword sets ────────────────────────────────────────────────────────────
_MOD_WORDS = (
    'public', 'private', 'protected', 'internal',
    'static', 'virtual', 'override', 'abstract',
    'sealed', 'async', 'extern', 'new', 'readonly',
    'volatile', 'partial', 'unsafe', 'const',
)

_BLACKLIST = frozenset([
    *_MOD_WORDS,
    'if', 'else', 'for', 'foreach', 'while', 'do',
    'switch', 'try', 'catch', 'finally', 'using', 'lock',
    'return', 'throw', 'break', 'continue', 'goto',
    'yield', 'await', 'case', 'default',
    'typeof', 'sizeof', 'nameof', 'checked', 'unchecked',
    'class', 'struct', 'interface', 'enum', 'namespace',
    'delegate', 'event', 'operator', 'implicit', 'explicit',
    'select', 'from', 'where', 'orderby', 'group', 'join',
])


# ── Regex building blocks ──────────────────────────────────────────────────────
_MOD = r'(?:(?:' + '|'.join(_MOD_WORDS) + r')\s+)*'

# Type: qualified name + optional generics (2 levels) + nullable + arrays
_TYPE_INNER = (
    r'[a-zA-Z_]\w*(?:\s*\.\s*[a-zA-Z_]\w*)*'          # base / qualified
    r'(?:\s*<[^<>()\n;{}]*(?:<[^<>()\n;{}]*>[^<>()\n;{}]*)*>)?'  # generics
    r'(?:\s*\?)?'                                       # nullable
    r'(?:\s*\[[\s,]*\])*'                               # arrays (int[], int[,])
)
_TYPE = rf'(?:void|var|{_TYPE_INNER})'
_ID   = r'[a-zA-Z_]\w*'

# Method: [mod*] return_type name(params)  { or => or ;
METHOD_RE = re.compile(
    rf'^\s*{_MOD}({_TYPE})\s+({_ID})\s*(\([^)\n]*\))\s*(?:where\b[^{{;\n]*)?\s*(?:{{|=>|;)',
    re.MULTILINE,
)

# Property: [mod*] type name {   (no ( before {)
PROPERTY_RE = re.compile(
    rf'^\s*{_MOD}({_TYPE})\s+({_ID})\s*\{{',
    re.MULTILINE,
)

# Field/variable: [mod*] type name  =  or  ;  or  ,
FIELD_RE = re.compile(
    rf'^\s*{_MOD}({_TYPE})\s+({_ID})\s*(?=[=;,])',
    re.MULTILINE,
)


# ── Comment / string removal ───────────────────────────────────────────────────
def remove_comments_and_strings(src: str) -> str:
    """Strip C# comments and string/char literals, preserving newlines."""
    out = []
    i, n = 0, len(src)

    while i < n:
        c = src[i]

        # Verbatim string @"..."
        if c == '@' and i + 1 < n and src[i + 1] == '"':
            i += 2
            while i < n:
                if src[i] == '"':
                    i += 1
                    if i < n and src[i] == '"':
                        i += 1          # escaped ""
                    else:
                        break
                else:
                    i += 1
            out.append(' ')
            continue

        # Regular string "..."
        if c == '"':
            i += 1
            while i < n and src[i] != '"':
                if src[i] == '\\':
                    i += 2
                else:
                    i += 1
            i += 1
            out.append(' ')
            continue

        # Char literal '.'
        if c == "'":
            i += 1
            while i < n and src[i] != "'":
                if src[i] == '\\':
                    i += 2
                else:
                    i += 1
            i += 1
            out.append(' ')
            continue

        # Single-line comment //
        if c == '/' and i + 1 < n and src[i + 1] == '/':
            while i < n and src[i] != '\n':
                i += 1
            continue

        # Block comment /* ... */
        if c == '/' and i + 1 < n and src[i + 1] == '*':
            i += 2
            while i + 1 < n:
                if src[i] == '*' and src[i + 1] == '/':
                    i += 2
                    break
                if src[i] == '\n':
                    out.append('\n')
                i += 1
            out.append(' ')
            continue

        out.append(c)
        i += 1

    return ''.join(out)


# ── Helpers ────────────────────────────────────────────────────────────────────
def normalize(s: str) -> str:
    return re.sub(r'\s+', ' ', s).strip()


def split_params(raw: str) -> list[str]:
    """Split parameter string by commas that are NOT inside < > brackets."""
    parts, depth, buf = [], 0, []
    for ch in raw:
        if ch == '<':
            depth += 1
            buf.append(ch)
        elif ch == '>':
            depth -= 1
            buf.append(ch)
        elif ch == ',' and depth == 0:
            parts.append(''.join(buf).strip())
            buf = []
        else:
            buf.append(ch)
    if buf:
        parts.append(''.join(buf).strip())
    return [p for p in parts if p]


def clean_params(raw: str) -> str:
    """Normalize parameter list: keep type+name, remove defaults & modifiers."""
    if not raw.strip():
        return ''
    parts = []
    for p in split_params(raw):
        p = re.sub(r'\s*=\s*.+$', '', p)                        # remove default value
        p = re.sub(r'^\s*(?:ref|out|in|params|this)\s+', '', p)  # remove param modifiers
        p = normalize(p)
        if p:
            parts.append(p)
    return ', '.join(parts)


def param_types_only(raw: str) -> str:
    """Return only the types from a parameter string, '-' if no parameters.

    'int amount, List<string> items' -> 'int, List<string>'
    """
    if not raw.strip():
        return '-'
    types = []
    for p in split_params(raw):
        p = re.sub(r'\s*=\s*.+$', '', p)                        # remove default value
        p = re.sub(r'^\s*(?:ref|out|in|params|this)\s+', '', p)  # remove param modifiers
        p = normalize(p)
        if not p:
            continue
        # Type is everything before the last identifier (the parameter name)
        m = re.match(r'^(.*)\s+[a-zA-Z_]\w*$', p)
        types.append(m.group(1).strip() if m else p)
    return ', '.join(types) if types else '-'


def is_valid(word: str) -> bool:
    return bool(word) and word not in _BLACKLIST


# ── Per-file extraction ────────────────────────────────────────────────────────
def extract_from_file(path: Path) -> list:
    try:
        src = path.read_text(encoding='utf-8', errors='ignore')
    except Exception as e:
        print(f'[WARN] {path}: {e}', file=sys.stderr)
        return []

    code = remove_comments_and_strings(src)
    filepath = str(path)
    filename = path.name
    seen: set = set()
    rows: list = []

    def add(kind_type: str, kind_name: str,
            in_types: str = '-', out_type: str = '-') -> None:
        key = (filepath, kind_type, kind_name)
        if key not in seen:
            seen.add(key)
            rows.append([filepath, filename, kind_type, kind_name, in_types, out_type])

    # Collect method match positions to avoid re-matching as fields/properties
    method_starts: set = set()

    # ── Methods ───────────────────────────────────────────────────────────────
    for m in METHOD_RE.finditer(code):
        ret        = normalize(m.group(1))
        name       = m.group(2)
        raw_params = m.group(3)[1:-1]              # strip outer parentheses
        params     = clean_params(raw_params)
        in_types   = param_types_only(raw_params)
        if not is_valid(ret) or not is_valid(name):
            continue
        method_starts.add(m.start())
        add(ret, f'{name}({params})', in_types, ret)

    # ── Properties ────────────────────────────────────────────────────────────
    for m in PROPERTY_RE.finditer(code):
        if m.start() in method_starts:
            continue
        t    = normalize(m.group(1))
        name = m.group(2)
        if not is_valid(t) or not is_valid(name):
            continue
        # Skip if there is a ( between the name and {  → it's a method
        between = code[m.start(2) + len(name) : m.end()]
        if '(' in between:
            continue
        add(t, name)   # in_types='-', out_type='-'

    # ── Fields / Variables ────────────────────────────────────────────────────
    for m in FIELD_RE.finditer(code):
        # Skip positions already matched as methods
        if any(abs(m.start() - s) < 5 for s in method_starts):
            continue
        t    = normalize(m.group(1))
        name = m.group(2)
        if not is_valid(t) or not is_valid(name):
            continue
        add(t, name)   # in_types='-', out_type='-'

    return rows


# ── Main ───────────────────────────────────────────────────────────────────────
def run(folder: str, output_csv: str) -> None:
    root = Path(folder)
    if not root.exists():
        print(f'Error: "{folder}" does not exist.', file=sys.stderr)
        sys.exit(1)

    cs_files = sorted(root.rglob('*.cs'))
    if not cs_files:
        print(f'No .cs files found in "{folder}".')
        return

    print(f'Found {len(cs_files)} .cs file(s). Processing...')
    all_rows: list = []

    for f in cs_files:
        rows = extract_from_file(f)
        all_rows.extend(rows)
        print(f'  [{len(rows):>4}]  {f}')

    out = Path(output_csv)
    with out.open('w', newline='', encoding='utf-8-sig') as fh:
        writer = csv.writer(fh)
        writer.writerow(['경로', '파일이름', '형식', '이름', '입력자료형', '출력자료형'])
        writer.writerows(all_rows)

    print(f'\nDone.  {len(all_rows)} rows  →  {output_csv}')


def main() -> None:
    if len(sys.argv) != 3:
        print('Usage:   python cs_extractor.py <folder_path> <output_csv>')
        print('Example: python cs_extractor.py ./MyProject output.csv')
        sys.exit(1)
    run(sys.argv[1], sys.argv[2])


if __name__ == '__main__':
    main()

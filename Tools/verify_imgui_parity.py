#!/usr/bin/env python3
# The MIT License (MIT)
# Copyright (c) 2014-2026 Omar Cornut
# Copyright (c) 2026 Least Action Technologies Inc.

import argparse
import json
import os
import re
import subprocess
import sys
from datetime import datetime, timezone


def strip_comments(text: str) -> str:
    """Remove C and C++ comments so that symbol regexes cannot match commented-out code."""
    # Remove block comments
    text = re.sub(r'/\*.*?\*/', '', text, flags=re.DOTALL)
    # Remove line comments
    text = re.sub(r'//.*$', '', text, flags=re.MULTILINE)
    return text


def strip_csharp_comments_and_literals(text: str) -> str:
    """Remove C# comments and literals in one ordered pass so symbol regexes match live code only.

    A single pass is required because regex-based stripping is order-sensitive: a character literal
    such as '"' is read as the start of a string when strings are removed first, and the phantom
    literal then swallows the declarations that follow it. Literal contents are replaced by spaces
    with newlines preserved, so token boundaries and line numbers remain intact.
    """
    out = []
    i = 0
    length = len(text)
    while i < length:
        c = text[i]
        nxt = text[i + 1] if i + 1 < length else ''
        if c == '#' and (i == 0 or text[i - 1] == '\n' or text[:i].rstrip(' \t').endswith('\n')):
            while i < length and text[i] != '\n':
                i += 1
            continue
        if c == '/' and nxt == '/':
            while i < length and text[i] != '\n':
                i += 1
            continue
        if c == '/' and nxt == '*':
            i += 2
            while i < length and not (text[i] == '*' and i + 1 < length and text[i + 1] == '/'):
                if text[i] == '\n':
                    out.append('\n')
                i += 1
            i += 2
            continue
        if text.startswith('"""', i):
            i += 3
            while i < length and not text.startswith('"""', i):
                if text[i] == '\n':
                    out.append('\n')
                i += 1
            i += 3
            continue
        if c == '@' and nxt == '"':
            i += 2
            while i < length:
                if text[i] == '"':
                    if i + 1 < length and text[i + 1] == '"':
                        i += 2
                        continue
                    i += 1
                    break
                if text[i] == '\n':
                    out.append('\n')
                i += 1
            continue
        if c == '"':
            i += 1
            while i < length:
                if text[i] == '\\':
                    i += 2
                    continue
                if text[i] == '"':
                    i += 1
                    break
                if text[i] == '\n':
                    out.append('\n')
                i += 1
            continue
        if c == "'":
            i += 1
            while i < length:
                if text[i] == '\\':
                    i += 2
                    continue
                if text[i] == "'":
                    i += 1
                    break
                if text[i] == '\n':
                    break
                i += 1
            continue
        out.append(c)
        i += 1
    return ''.join(out)


# --------------------------------------------------------------------------- canonical headers

# A struct or class declaration in a C++ header. The opening brace is located by scanning
# forward so that base-class clauses and attributes do not defeat the match.
_CPP_TYPE_DECL = re.compile(r'\b(?:struct|class)\s+([A-Za-z_]\w*)')

# An exported function declaration. Deliberately permissive about the return type so that
# pointer, reference and template returns are all captured.
_CPP_FUNCTION_DECL = re.compile(r'IMGUI_API\s+([A-Za-z_][\w\s\*&:<>,]*?)\s+([A-Za-z_]\w*)\s*\(')


def extract_cpp_type_ranges(header_content: str):
    """Return the struct and class bodies of a comment-stripped C++ header.

    Base-class clauses, attributes and forward declarations are handled by scanning forward
    from each declaration to its opening brace and then matching braces.

    Args:
        header_content: Comment-stripped header text.

    Returns:
        A list of ``(type_name, body_start, body_end)`` tuples, one per definition, where the
        offsets are absolute indices into ``header_content`` and bracket the body only.
    """
    ranges = []
    for match in _CPP_TYPE_DECL.finditer(header_content):
        cursor = match.end()
        while cursor < len(header_content) and header_content[cursor] != '{':
            if header_content[cursor] == ';':  # Forward declaration, not a definition.
                cursor = None
                break
            cursor += 1
        if cursor is None or cursor >= len(header_content):
            continue
        depth = 0
        for index in range(cursor, len(header_content)):
            if header_content[index] == '{':
                depth += 1
            elif header_content[index] == '}':
                depth -= 1
                if depth == 0:
                    ranges.append((match.group(1), cursor, index))
                    break
    return ranges


def owner_at_position(type_ranges, position: int):
    """Return the innermost struct or class body containing an absolute source position.

    Args:
        type_ranges: Ranges from :func:`extract_cpp_type_ranges`.
        position: Absolute offset into the same comment-stripped header text.

    Returns:
        The owning type name, or ``None`` when the position is at namespace scope.
    """
    owner = None
    width = None
    for name, start, end in type_ranges:
        if start < position < end and (width is None or (end - start) < width):
            owner = name
            width = end - start
    return owner


def extract_enums(header_content: str):
    """Extract canonical enum names and their member names from a comment-stripped header."""
    enums = {}
    pattern = re.compile(r'enum\s+([A-Za-z0-9_]+)\s*(?::\s*[A-Za-z0-9_]+)?\s*\{([^}]+)\};', re.MULTILINE)

    for match in pattern.finditer(header_content):
        enum_name = match.group(1)
        body = match.group(2)
        members = []
        for line in body.split(','):
            line = line.strip()
            if not line or line.startswith('#'):
                continue
            parts = line.split('=')
            member_name = parts[0].strip()
            # Clean up macro wrappers or directives
            if re.match(r'^[A-Za-z0-9_]+$', member_name):
                member_val = parts[1].strip() if len(parts) > 1 else None
                members.append({"name": member_name, "value": member_val})
        if members:
            # Normalize enum name (e.g. ImGuiWindowFlags_ -> ImGuiWindowFlags)
            clean_name = enum_name[:-1] if enum_name.endswith('_') else enum_name
            enums[clean_name] = members

    return enums


def extract_structs(header_content: str):
    """Extract canonical struct names and their field names from a comment-stripped header."""
    structs = {}
    pattern = re.compile(r'struct\s+([A-Za-z0-9_]+)\s*(?:;|\{([^}]+)\};)', re.MULTILINE)
    for match in pattern.finditer(header_content):
        name = match.group(1)
        body = match.group(2)
        fields = []
        if body:
            for line in body.split(';'):
                line = line.strip()
                if not line or line.startswith('//') or line.startswith('#') or '(' in line:
                    continue
                tokens = line.split()
                if len(tokens) >= 2:
                    field_name = tokens[-1].strip('*&')
                    field_type = ' '.join(tokens[:-1])
                    if re.match(r'^[A-Za-z0-9_]+$', field_name):
                        fields.append({"name": field_name, "type": field_type})
        structs[name] = fields
    return structs


def extract_functions(header_content: str, header_filename: str, type_ranges):
    """Extract `IMGUI_API` declarations and attribute each to its owning type.

    Args:
        header_content: Comment-stripped header text.
        header_filename: Header file name recorded on each entry.
        type_ranges: Ranges from :func:`extract_cpp_type_ranges` for the same text.

    Returns:
        A list of entries with ``name``, ``return_type``, ``parameters``, ``category``,
        ``header``, ``owner`` and ``kind``. ``kind`` is ``constructor`` when the declaration has
        no return type (the header extractor captures those with an empty return type),
        ``member_function`` when the owner is a struct or class, and ``free_function`` otherwise.
    """
    functions = []
    pattern = re.compile(
        r'IMGUI_API\s+([A-Za-z0-9_*&:\s]+?)\s+([A-Za-z0-9_]+)\s*\((.*?)\)\s*;',
        re.DOTALL
    )

    docking_keywords = {"dock", "dockspace", "dockbuilder", "docknode"}

    # Ordered subsystem rules. Each canonical function is classified into the plan subsystem that owns
    # it so the report surfaces per-subsystem gaps instead of burying most of the API in one bucket.
    # The first matching rule wins; patterns are anchored where a loose prefix would misclassify.
    category_rules = (
        ("Tables", r'^(BeginTable|EndTable|Table)'),
        ("Tabs", r'^(BeginTabBar|EndTabBar|BeginTabItem|EndTabItem|TabItem|SetTabItemClosed)'),
        ("Columns", r'^(Columns|BeginColumns|EndColumns|GetColumn|SetColumn|TableGetColumn|TableSetColumn|TableNext)'),
        ("DragDrop", r'^(BeginDragDrop|EndDragDrop|AcceptDragDrop|SetDragDrop|GetDragDrop)'),
        ("MultiSelect", r'(MultiSelect|Selection|ToggledSelection|BoxSelect)'),
        ("Tooltips", r'^(BeginTooltip|EndTooltip|SetTooltip|BeginItemTooltip|SetItemTooltip)'),
        ("Menus", r'^(BeginMenu|EndMenu|MenuItem|BeginMainMenuBar|EndMainMenuBar|BeginMenuBar|EndMenuBar)'),
        ("Popups", r'^(BeginPopup|EndPopup|OpenPopup|ClosePopup|CloseCurrentPopup|IsPopupOpen)'),
        ("Trees", r'^(TreeNode|TreePush|TreePop|CollapsingHeader|SetNextItemOpen|GetTreeNode)'),
        ("FontAtlas", r'(Font|GlyphRanges|CustomRect|GetTexData)'),
        ("Settings", r'(Settings|Ini|LocalizeRegisterEntries)'),
        ("Viewports", r'(Viewport|Platform)'),
        ("MetricsDebug", r'(Metrics|Debug|IDStackTool|^Log)'),
        ("Helpers", r'(ListClipper|TextFilter|TextBuffer|Storage|OnceUponAFrame|DataType|ColorConvert|TypingSelect|CallbackData)'),
        ("DrawList", r'^(AddLine|AddRect|AddCircle|AddTriangle|AddQuad|AddNgon|AddEllipse|AddBezier|AddPolyline|AddConvex|AddConcave|AddImage|AddText|AddCallback|AddDrawCmd|AddCustomRect|PushClipRect|PopClipRect|PushTexture|PopTexture|Path|Prim|ShadeVerts)'),
        ("Demo", r'^(ShowDemoWindow|ShowAboutWindow|ShowStyleEditor|ShowUserGuide|ShowExampleApp)'),
        ("InputWidgets", r'^(Drag|Slider|VSlider|Input)'),
        ("CoreWidgets", r'^(Button|Checkbox|RadioButton|Text|LabelText|Bullet|Selectable|Combo|ListBox|Image|ProgressBar|Plot|ColorEdit|ColorPicker|ColorButton|SetColorEditOptions|Value|Separator|Spacing|Dummy|Indent|Unindent|SameLine|NewLine|AlignText)'),
        ("WidgetSupport", r'^(Render[A-Z]|ItemAdd|ItemHoverable|ItemSize|CalcItem|CalcText|CalcWrap|MarkItemEdited|PushItemWidth|PopItemWidth|PushTextWrapPos|PopTextWrapPos|PushMultiItemsWidths|IsItem|IsClipped|IsRectVisible|FindRenderedTextEnd)'),
        ("Windows", r'^(Begin|End|SetNextWindow|GetWindow|IsWindow|FocusWindow|BringWindow|FindWindow|CalcWindow|SetWindow|GetContentRegion|GetWindow)'),
        ("Context", r'^(CreateContext|DestroyContext|GetCurrentContext|SetCurrentContext|NewFrame|Render$|EndFrame|GetIO|GetStyle|GetTime|GetFrameCount|GetDrawData|GetMainViewport|Memory|Alloc|GetVersion|AddKey|AddMouse|AddInput|AddFocus|ClearInput|IsKey|GetKey|SetNav|GetNav|Nav|Error|Gc)'),
        ("IdStack", r'^(PushID|PopID|GetID|PushOverrideID|GetHoveredID|ClearActiveID|KeepAliveID)'),
    )

    for match in pattern.finditer(header_content):
        ret_type = ' '.join(match.group(1).split())
        func_name = match.group(2).strip()
        params_raw = ' '.join(match.group(3).split())

        # Determine subsystem category.
        lower_name = func_name.lower()
        if any(dk in lower_name for dk in docking_keywords):
            category = "Docking"
        else:
            category = "General"
            for candidate, regex in category_rules:
                if re.search(regex, func_name):
                    category = candidate
                    break

        if ret_type:
            owner = owner_at_position(type_ranges, match.start()) or 'ImGui'
            kind = 'member_function' if owner != 'ImGui' else 'free_function'
        else:
            # A declaration without a return type is a constructor; its owner is the type it
            # constructs, which is also its name.
            owner = func_name
            kind = 'constructor'

        functions.append({
            "name": func_name,
            "return_type": ret_type,
            "parameters": params_raw,
            "category": category,
            "header": header_filename,
            "owner": owner,
            "kind": kind
        })

    return functions


def extract_struct_field_names(header_content: str):
    """Extract every struct definition's field names using brace matching.

    The line-oriented parser used for the symbol manifest cannot see fields inside structures that
    contain inline methods, nested types, or preprocessor branches, and a missing field produces no
    missing symbol at all. This routine exists so the parity report can state, per structure, which
    canonical fields have no C# counterpart instead of leaving them to be discovered by hand.
    """
    fields = {}
    for match in re.finditer(r'\bstruct\s+([A-Za-z_][A-Za-z0-9_]*)\b', header_content):
        name = match.group(1)
        if name in fields:
            continue
        cursor = match.end()
        while cursor < len(header_content) and header_content[cursor] in ' \t\r\n':
            cursor += 1
        if cursor >= len(header_content) or header_content[cursor] != '{':
            continue
        depth = 0
        end = None
        for k in range(cursor, len(header_content)):
            if header_content[k] == '{':
                depth += 1
            elif header_content[k] == '}':
                depth -= 1
                if depth == 0:
                    end = k
                    break
        if end is None:
            continue
        names = []
        for raw_line in header_content[cursor:end].split('\n'):
            line = raw_line.split('//')[0].strip()
            if not line or line.startswith('#') or '(' in line:
                continue
            if line.startswith(('struct ', 'union ', 'enum ', '};', '}')):
                continue
            line = re.sub(r':\s*\d+\s*$', '', line).rstrip()
            if not line.endswith(';'):
                continue
            declaration = line[:-1].strip()
            declarator = re.match(r'^(.+?)\b([A-Za-z_]\w*)\s*(\[[^\]]*\])?$', declaration)
            if not declarator:
                continue
            type_part = declarator.group(1).strip()
            if not type_part or type_part.endswith(('struct', 'union', 'enum')):
                continue
            names.append(declarator.group(2))
        if names:
            fields[name] = names
    return fields


def extract_symbols(reference_dir: str):
    """Build the canonical symbol manifest from the reference checkout's two public headers."""
    imgui_h_path = os.path.join(reference_dir, "imgui.h")
    imgui_internal_h_path = os.path.join(reference_dir, "imgui_internal.h")

    if not os.path.isfile(imgui_h_path) or not os.path.isfile(imgui_internal_h_path):
        raise FileNotFoundError(f"Missing header files in reference directory: {reference_dir}")

    with open(imgui_h_path, 'r', encoding='utf-8', errors='replace') as f:
        imgui_h_raw = f.read()

    with open(imgui_internal_h_path, 'r', encoding='utf-8', errors='replace') as f:
        imgui_internal_h_raw = f.read()

    clean_imgui_h = strip_comments(imgui_h_raw)
    clean_internal_h = strip_comments(imgui_internal_h_raw)

    enums = {}
    enums.update(extract_enums(clean_imgui_h))
    enums.update(extract_enums(clean_internal_h))

    structs = {}
    structs.update(extract_structs(clean_imgui_h))
    structs.update(extract_structs(clean_internal_h))

    struct_fields = {}
    struct_fields.update(extract_struct_field_names(clean_imgui_h))
    struct_fields.update(extract_struct_field_names(clean_internal_h))

    functions = []
    functions.extend(extract_functions(clean_imgui_h, "imgui.h", extract_cpp_type_ranges(clean_imgui_h)))
    functions.extend(extract_functions(clean_internal_h, "imgui_internal.h",
                                       extract_cpp_type_ranges(clean_internal_h)))

    # De-duplicate overloaded function names while tracking variations
    func_dict = {}
    for fn in functions:
        func_dict.setdefault(fn["name"], []).append(fn)

    kinds = {}
    for overloads in func_dict.values():
        kind = overloads[0]["kind"]
        kinds[kind] = kinds.get(kind, 0) + 1

    # Git metadata from reference directory if it is a git repo
    commit_sha = "unknown"
    commit_desc = "unknown"
    git_dir = os.path.join(reference_dir, ".git")
    if os.path.isdir(git_dir):
        try:
            res = subprocess.run(["git", "rev-parse", "HEAD"], cwd=reference_dir, capture_output=True, text=True)
            if res.returncode == 0:
                commit_sha = res.stdout.strip()
            res2 = subprocess.run(["git", "log", "-1", "--oneline"], cwd=reference_dir, capture_output=True, text=True)
            if res2.returncode == 0:
                commit_desc = res2.stdout.strip()
        except Exception:
            pass

    manifest = {
        "metadata": {
            "canonical_repository": "https://github.com/ocornut/imgui",
            "commit_sha": commit_sha,
            "commit_description": commit_desc,
            "extracted_at": datetime.now(timezone.utc).isoformat(),
            "counts": {
                "unique_functions": len(func_dict),
                "total_function_overloads": len(functions),
                "enums": len(enums),
                "structs": len(structs),
                "free_functions": kinds.get("free_function", 0),
                "member_functions": kinds.get("member_function", 0),
                "constructors": kinds.get("constructor", 0)
            }
        },
        "enums": enums,
        "structs": structs,
        "struct_fields": struct_fields,
        "functions": func_dict
    }

    return manifest


# --------------------------------------------------------------------------- C# surface audit

# Type declarations whose members this audit attributes. Nested declarations are tracked by the
# brace scan in :func:`scan_csharp_types`.
_CSHARP_TYPE_DECL = re.compile(
    r'(?:public|internal|protected|private)?\s*'
    r'(?:static\s+|sealed\s+|abstract\s+|partial\s+|readonly\s+|ref\s+|unsafe\s+|new\s+)*'
    r'(class|struct|record|enum|interface)\s+([A-Za-z_]\w*)')

# Methods that can realize a canonical function. Private and protected members are excluded:
# they cannot be part of the port's functional surface, and admitting them would let unrelated
# helpers claim canonical names.
_CSHARP_METHOD = re.compile(
    r'\b(public|internal)\s+'
    r'(?:static\s+|virtual\s+|override\s+|sealed\s+|abstract\s+|async\s+|unsafe\s+|extern\s+|'
    r'new\s+|partial\s+|readonly\s+)*'
    r'[A-Za-z_][\w<>,\[\]\.\?]*\s+([A-Za-z_]\w*)\s*\(')

# Fields and properties. The type part permits spaces and nested generic arguments so that
# Dictionary<uint, ImGuiWindow> is read as one type, and permits nullable suffixes.
_CSHARP_MEMBER = re.compile(
    r'\b(public|internal)\s+'
    r'(?:static\s+|readonly\s+|const\s+|volatile\s+|new\s+|unsafe\s+|virtual\s+|override\s+|'
    r'sealed\s+|abstract\s+|partial\s+|required\s+)*'
    r'([A-Za-z_][\w\.\?]*(?:\s*<[^;{}()]*>\s*\??)?(?:\s*\[\s*\]\s*\??)*)\s+'
    r'([A-Za-z_]\w*)\s*(?=[\{;=,\(]|=>)')

_CONST_ALLOWED = re.compile(r'^[\s0-9A-Fa-fxX()<>|&~+\-*/%.,]+$')


def evaluate_constant(expression, resolved):
    """Evaluate an integer constant expression used as an enum member value.

    Member references are substituted from ``resolved``, which must therefore contain the values
    already read in declaration order.

    Args:
        expression: Raw value text, or ``None`` when the member has no explicit value.
        resolved: Mapping of previously resolved member names to integer values.

    Returns:
        The evaluated integer, or ``None`` when the expression cannot be evaluated. Callers must
        treat ``None`` as "value unknown" and compare member presence only. The result may be
        signed or unsigned depending on how the expression is written, so callers comparing C++
        and C# values must compare 32-bit patterns rather than raw equality.
    """
    if expression is None:
        return None
    text = expression.strip()
    if not text:
        return None
    text = re.sub(r'\bunchecked\b', '', text)
    text = re.sub(r'\((?:u?int|u?long|u?short|u?byte|sbyte)\)', '', text)
    text = text.replace('uint.MaxValue', str(2**32 - 1)).replace('int.MaxValue', str(2**31 - 1))
    text = text.replace('long.MaxValue', '9223372036854775807').replace('ulong.MaxValue', '18446744073709551615')
    text = re.sub(r'\b([A-Za-z_]\w*)\b',
                  lambda m: str(resolved[m.group(1)]) if m.group(1) in resolved else m.group(0),
                  text)
    if not _CONST_ALLOWED.match(text):
        return None
    try:
        return int(eval(text, {'__builtins__': {}}, {}))
    except Exception:
        return None


def scan_csharp_types(text: str):
    """Return the type declarations of one comment-stripped C# file.

    Args:
        text: Comment- and literal-stripped C# source.

    Returns:
        A list of dictionaries with ``kind``, ``name``, ``start`` and ``end``, where the offsets
        bracket each type's brace-delimited body. Declarations are returned innermost-last in
        source order.
    """
    events = []
    for match in _CSHARP_TYPE_DECL.finditer(text):
        events.append((match.start(), 0, 'type', match.group(1), match.group(2)))
    for match in re.finditer(r'[{};]', text):
        events.append((match.start(), 1, 'punct', match.group(0), None))
    events.sort(key=lambda entry: (entry[0], entry[1]))

    stack, pending, found = [], None, []
    for _position, _order, kind, first, second in events:
        if kind == 'type':
            pending = (first, second)
        elif first == '{':
            stack.append((pending, _position) if pending else None)
            pending = None
        elif first == '}':
            if stack:
                entry = stack.pop()
                if entry:
                    found.append({'kind': entry[0][0], 'name': entry[0][1],
                                  'start': entry[1], 'end': _position})
        else:
            pending = None
    return found


def _nesting_parents(types):
    """Return a mapping from each type entry to its directly enclosing entry, or ``None``."""
    ordered = sorted(types, key=lambda entry: (entry['start'], -entry['end']))
    stack, parents = [], {}
    for entry in ordered:
        while stack and stack[-1]['end'] < entry['start']:
            stack.pop()
        parents[id(entry)] = stack[-1] if stack else None
        stack.append(entry)
    return parents


def _blank_ranges(text, base_start, base_end, ranges):
    """Return ``text[base_start:base_end]`` with the given absolute ranges replaced by spaces.

    Newlines are preserved so that the result keeps the surrounding declarations on their
    original lines. Used to hide nested type bodies before a parent type is scanned, so a nested
    type's members are not also attributed to its parent.
    """
    chars = list(text[base_start:base_end])
    for start, end in ranges:
        for index in range(max(start, base_start), min(end, base_end)):
            if chars[index - base_start] != '\n':
                chars[index - base_start] = ' '
    return ''.join(chars)


def parse_enum_members(body: str):
    """Return ``[(member_name, raw_value)]`` for one C# enum body (the text after its brace).

    Commas nested inside parentheses, casts or attributes do not separate members.
    """
    members, buffer, depth = [], '', 0
    for char in body:
        if char in '(<[':
            depth += 1
        elif char in ')>]':
            depth -= 1
        if char == ',' and depth == 0:
            members.append(buffer)
            buffer = ''
        else:
            buffer += char
    if buffer.strip():
        members.append(buffer)

    parsed = []
    for member in members:
        member = member.strip()
        if not member:
            continue
        if '=' in member:
            name, value = member.split('=', 1)
        else:
            name, value = member, None
        name = name.strip()
        if re.match(r'^[A-Za-z_]\w*$', name):
            parsed.append((name, value.strip() if value else None))
    return parsed


def extract_csharp_surface(source_root: str):
    """Build a type-attributed inventory of the C# surface under a source root.

    Every ``public`` and ``internal`` method and data member is attributed to its declaring type;
    declarations of one partial type across files are merged. Enum members are recorded with their
    evaluated value.

    Args:
        source_root: Directory scanned recursively for ``.cs`` files.

    Returns:
        A dictionary with ``types`` (name to ``{'methods': {name: {'count', 'public'}},
        'members': set(), 'kinds': set()}``) and ``enums`` (name to member-to-value mapping).
        ``bin`` and ``obj`` directories are skipped.
    """
    types = {}
    enums = {}

    for directory, subdirectories, filenames in os.walk(source_root):
        subdirectories[:] = [name for name in subdirectories if name not in ('bin', 'obj')]
        for filename in filenames:
            if not filename.endswith('.cs'):
                continue
            path = os.path.join(directory, filename)
            with open(path, 'r', encoding='utf-8', errors='replace') as handle:
                text = strip_csharp_comments_and_literals(handle.read())

            found = scan_csharp_types(text)
            parents = _nesting_parents(found)
            for entry in found:
                record = types.setdefault(entry['name'],
                                          {'methods': {}, 'members': set(), 'kinds': set()})
                record['kinds'].add(entry['kind'])
                if entry['kind'] == 'enum':
                    body = text[entry['start']:entry['end']]
                    brace = body.find('{')
                    resolved, values = {}, {}
                    for name, raw in parse_enum_members(body[brace + 1:]):
                        value = evaluate_constant(raw, resolved) if raw is not None else (
                            max(resolved.values()) + 1 if resolved else 0)
                        values[name] = value
                        if value is not None:
                            resolved[name] = value
                    enums.setdefault(entry['name'], {}).update(values)
                    continue

                nested = [other for other in found if parents.get(id(other)) is entry]
                body = _blank_ranges(text, entry['start'], entry['end'],
                                     [(other['start'], other['end']) for other in nested])
                for accessibility, name in _CSHARP_METHOD.findall(body):
                    method = record['methods'].setdefault(name, {'count': 0, 'public': False})
                    method['count'] += 1
                    method['public'] = method['public'] or accessibility == 'public'
                for _accessibility, _type, name in _CSHARP_MEMBER.findall(body):
                    record['members'].add(name)

    return {'types': types, 'enums': enums}


# --------------------------------------------------------------------------- coverage

def _normalize_field(name):
    """Normalize a field name for cross-language comparison.

    The port exposes canonical C++ fields as PascalCase members and occasionally prefixes backing
    storage with an underscore; comparison is therefore case-insensitive with leading underscores
    removed.
    """
    return name.lstrip('_').lower()


def compute_coverage(manifest: dict, surface: dict, source_root: str, assembly_path: str):
    """Compare the canonical manifest against the C# surface and summarize coverage.

    Args:
        manifest: Canonical manifest from :func:`extract_symbols`.
        surface: C# inventory from :func:`extract_csharp_surface`.
        source_root: Source root recorded in the report.
        assembly_path: Build artifact recorded in the report, or ``None``.

    Returns:
        A dictionary describing function, enum and struct coverage, including the names of every
        symbol that is missing or attributed to the wrong type.
    """
    types = surface['types']
    csharp_enums = surface['enums']

    declared_anywhere = {}
    for type_name, record in types.items():
        for method_name in record['methods']:
            declared_anywhere.setdefault(method_name, []).append(type_name)

    constructors = 0

    # Canonical coverage is measured per (name, owner) binding. A canonical name may be owned by
    # more than one type (PushClipRect is both a free ImGui:: function and an ImDrawList member),
    # and each binding has to be satisfied separately.
    bindings = {}
    for name, overloads in manifest['functions'].items():
        if overloads[0].get('kind') == 'constructor':
            constructors += 1
            continue
        for overload in overloads:
            key = (name, overload.get('owner'))
            bindings[key] = bindings.get(key, 0) + 1

    binding_totals = {'free_function': [0, 0], 'member_function': [0, 0]}
    per_name = {}
    for (name, owner), _overload_count in bindings.items():
        kind = 'free_function' if owner == 'ImGui' else 'member_function'
        binding_totals[kind][0] += 1
        record = types.get(owner)
        method = record['methods'].get(name) if record else None
        if method is None and owner == 'ImGui' and 'ImGuiDemo' in types:
            category = manifest['functions'].get(name, [{}])[0].get('category')
            if category == 'Demo':
                demo_record = types.get('ImGuiDemo')
                method = demo_record['methods'].get(name) if demo_record else None
        info = per_name.setdefault(name, {'owners': 0, 'matched': 0, 'public': False,
                                          'missing_owners': []})
        info['owners'] += 1
        if method is not None:
            binding_totals[kind][1] += 1
            info['matched'] += 1
            info['public'] = info['public'] or method['public']
        else:
            info['missing_owners'].append(owner)

    implemented, partial, public_exposed, misplaced, missing = [], [], [], [], []
    for name, info in per_name.items():
        if info['matched'] == info['owners']:
            implemented.append(name)
            if info['public']:
                public_exposed.append(name)
        elif info['matched'] > 0:
            partial.append({'name': name, 'missing_owners': info['missing_owners']})
            if info['public']:
                public_exposed.append(name)
        else:
            owners = [owner for owner_name, owner in bindings if owner_name == name]
            if all(types.get(owner) is None for owner in owners):
                # No canonical owning type exists in C# yet, so a same-named member elsewhere is a
                # coincidence rather than coverage.
                missing.append({'name': name, 'owner': ', '.join(owners),
                                'collision': declared_anywhere.get(name, [])[:4]})
            elif name in declared_anywhere:
                misplaced.append({'name': name, 'owner': ', '.join(owners),
                                  'found_on': declared_anywhere[name][:4]})
            else:
                missing.append({'name': name, 'owner': ', '.join(owners), 'collision': []})

    overload_gaps = []
    for name in list(implemented) + [entry['name'] for entry in partial]:
        owners = [owner for owner_name, owner in bindings if owner_name == name]
        canonical_overloads = len(manifest['functions'][name])
        csharp_overloads = 0
        for owner in owners:
            record = types.get(owner)
            if record and name in record['methods']:
                csharp_overloads += record['methods'][name]['count']
            elif owner == 'ImGui' and 'ImGuiDemo' in types and name in types['ImGuiDemo']['methods']:
                category = manifest['functions'].get(name, [{}])[0].get('category')
                if category == 'Demo':
                    csharp_overloads += types['ImGuiDemo']['methods'][name]['count']
        if csharp_overloads < canonical_overloads:
            overload_gaps.append({'name': name, 'canonical': canonical_overloads,
                                  'csharp': csharp_overloads})

    implemented_set = set(implemented) | {entry['name'] for entry in partial}
    categories = {}
    for name, overloads in manifest['functions'].items():
        if overloads[0].get('kind') == 'constructor':
            continue
        category = overloads[0]['category']
        entry = categories.setdefault(category, {'canonical': 0, 'implemented': 0,
                                                 'public': 0, 'missing': 0})
        entry['canonical'] += 1
        if name in implemented_set:
            entry['implemented'] += 1
            if name in public_exposed:
                entry['public'] += 1
        else:
            entry['missing'] += 1

    unresolved = sorted({name for name, overloads in manifest['functions'].items()
                         for overload in overloads if not overload.get('owner')})

    # Enum coverage: member names and evaluated values.
    enum_members_canonical = 0
    enum_members_missing = []
    enum_value_mismatches = []
    for enum_name, members in manifest['enums'].items():
        canonical_values = {}
        for member in members:
            enum_members_canonical += 1
            canonical_values[member['name']] = member['value']
        values = csharp_enums.get(enum_name)
        if values is None:
            enum_members_missing.append({'enum': enum_name, 'members': sorted(canonical_values),
                                         'type_absent': True})
            continue
        absent = [name for name in canonical_values if name not in values]
        if absent:
            enum_members_missing.append({'enum': enum_name, 'members': sorted(absent),
                                         'type_absent': False})
        for name, raw in canonical_values.items():
            wanted = evaluate_constant(raw, {})
            got = values.get(name)
            if wanted is None or got is None or wanted == got:
                continue
            if (wanted & 0xFFFFFFFF) == (got & 0xFFFFFFFF):
                continue  # Signedness-only difference.
            enum_value_mismatches.append({'enum': enum_name, 'member': name,
                                          'canonical': raw, 'csharp': got})

    # Struct coverage: type presence over every canonical struct, then per-structure field names
    # for the structures that do exist in C#.
    enum_member_names = {member['name'] for members in manifest['enums'].values() for member in members}
    fields_checked = 0
    absent_structs = []
    field_gaps = []
    for struct_name in manifest['structs']:
        record = types.get(struct_name)
        if record is None:
            absent_structs.append({'name': struct_name,
                                   'fields': manifest['struct_fields'].get(struct_name, [])})
            continue
        fields = [name for name in manifest['struct_fields'].get(struct_name, [])
                  if name not in ('true', 'false') and name not in enum_member_names]
        if not fields:
            continue
        fields_checked += len(fields)
        present = {_normalize_field(name) for name in record['members']}
        absent = [name for name in fields if _normalize_field(name) not in present]
        if absent:
            field_gaps.append({'name': struct_name, 'fields': absent})

    return {
        'source_root': source_root,
        'assembly': assembly_path or '(not supplied)',
        'functions': {
            'canonical_names': len(per_name),
            'constructors_excluded': constructors,
            'bindings': binding_totals,
            'implemented': len(implemented_set),
            'fully_implemented': len(implemented),
            'partial': partial,
            'public_exposed': len(public_exposed),
            'misplaced': misplaced,
            'missing': missing,
            'unresolved_owners': unresolved,
            'overload_gaps': overload_gaps,
        },
        'categories': categories,
        'enums': {
            'canonical_types': len(manifest['enums']),
            'present_types': sum(1 for name in manifest['enums'] if name in csharp_enums),
            'canonical_members': enum_members_canonical,
            'missing_members': enum_members_missing,
            'value_mismatches': enum_value_mismatches,
        },
        'structs': {
            'canonical_types': len(manifest['structs']),
            'present_types': sum(1 for name in manifest['struct_fields'] if name in types),
            'fields_checked': fields_checked,
            'absent_types': absent_structs,
            'field_gaps': field_gaps,
        },
    }


# --------------------------------------------------------------------------- reporting

def _percent(matched, canonical):
    """Format a coverage percentage, or ``0.0%`` when there is nothing to cover."""
    return f'{(100.0 * matched / canonical) if canonical else 0.0:.1f}%'


def print_coverage_report(coverage: dict) -> None:
    """Print the measured coverage categorization to standard output.

    Args:
        coverage: Coverage dictionary from :func:`compute_coverage`.

    Returns:
        None. The report is written to standard output; no file is created.
    """
    functions = coverage['functions']
    free = functions['bindings']['free_function']
    member = functions['bindings']['member_function']
    enums = coverage['enums']
    structs = coverage['structs']

    print("\n== Subsystem coverage (implemented/canonical) ==")
    for category in sorted(coverage['categories']):
        entry = coverage['categories'][category]
        print(f"  {category:16s} {entry['implemented']:4d}/{entry['canonical']:<4d} "
              f"{_percent(entry['implemented'], entry['canonical']):>6s}  "
              f"public {entry['public']}")

    print("\n== Symbol totals ==")
    print(f"  Free ImGui:: bindings   {free[1]}/{free[0]} ({_percent(free[1], free[0])})")
    print(f"  Struct member bindings  {member[1]}/{member[0]} ({_percent(member[1], member[0])})")
    print(f"  Function names          {functions['implemented']}/{functions['canonical_names']} "
          f"({_percent(functions['implemented'], functions['canonical_names'])}) "
          f"[{functions['fully_implemented']} fully, {len(functions['partial'])} partial, "
          f"{functions['public_exposed']} with a public member]")
    print(f"  Enums                   {enums['present_types']}/{enums['canonical_types']}")
    missing_enum_members = sum(len(entry['members']) for entry in enums['missing_members'])
    print(f"  Enum members            {enums['canonical_members'] - missing_enum_members}/"
          f"{enums['canonical_members']}")
    print(f"  Structs                 {structs['present_types']}/{structs['canonical_types']}")
    missing_fields = sum(len(gap['fields']) for gap in structs['field_gaps']) \
        + sum(len(entry['fields']) for entry in structs['absent_types'])
    print(f"  Struct fields           {structs['fields_checked'] - missing_fields}/"
          f"{structs['fields_checked']}")
    print(f"  Canonical constructors excluded: {functions['constructors_excluded']}")

    if functions['partial']:
        print("\n== Partially implemented names ==")
        for entry in sorted(functions['partial'], key=lambda item: item['name']):
            print(f"  {entry['name']}: unmatched owner {', '.join(entry['missing_owners'])}")

    if functions['misplaced']:
        print("\n== Misplaced symbols (name present on a non-owning type) ==")
        for entry in sorted(functions['misplaced'], key=lambda item: item['name']):
            print(f"  {entry['name']}: canonical owner {entry['owner']}, "
                  f"found on {', '.join(entry['found_on'])}")

    if functions['overload_gaps']:
        print("\n== Overload gaps (review individually; optional parameters can be canonical) ==")
        for gap in sorted(functions['overload_gaps'], key=lambda item: item['name']):
            print(f"  {gap['name']}: canonical {gap['canonical']}, C# {gap['csharp']}")

    if enums['missing_members'] or enums['value_mismatches']:
        print("\n== Enum gaps ==")
        for entry in enums['missing_members']:
            print(f"  {entry['enum']}: missing {', '.join(entry['members'])}")
        for entry in enums['value_mismatches']:
            print(f"  {entry['enum']}.{entry['member']}: canonical {entry['canonical']}, "
                  f"C# {entry['csharp']}")

    if structs['absent_types']:
        print("\n== Canonical structs with no C# type ==")
        print("  " + ", ".join(entry['name'] for entry in structs['absent_types']))

    if structs['field_gaps']:
        print("\n== Structures with missing fields ==")
        for entry in structs['field_gaps']:
            print(f"  {entry['name']}: {', '.join(entry['fields'])}")

    if functions['missing']:
        print(f"\n== Missing function names: {len(functions['missing'])} "
              f"(use --json to enumerate them) ==")


def main():
    """Parse command-line arguments, extract canonical symbols, audit C# coverage, and print it."""
    parser = argparse.ArgumentParser(description="Extract canonical ImGui symbols and verify parity.")
    parser.add_argument("--reference", default="ThirdParty/imgui", help="Path to reference checkout")
    parser.add_argument("--output-manifest", default="Tools/imgui_symbols_v1.93.0.json", help="Path to output JSON manifest")
    parser.add_argument("--assembly", default=None, help="Path to the built Hikari.ImGui.dll recorded as the audited build artifact")
    parser.add_argument("--source-root", default="Hikari.ImGui", help="C# source root scanned for measured coverage")
    parser.add_argument("--demo-root", default="Hikari.ImGui.Demo", help="Optional C# demo root scanned for demo-category coverage (keeps demo out of core library)")
    parser.add_argument("--json", default=None, help="Optional path for a JSON dump of the measured coverage")

    args = parser.parse_args()

    print(f"[*] Extracting canonical symbols from {args.reference}...")
    try:
        manifest = extract_symbols(args.reference)
        os.makedirs(os.path.dirname(os.path.abspath(args.output_manifest)), exist_ok=True)
        with open(args.output_manifest, "w", encoding="utf-8") as f:
            json.dump(manifest, f, indent=2)
        print(f"[+] Canonical manifest written to {args.output_manifest}")

        coverage = None
        if args.source_root and os.path.isdir(args.source_root):
            print(f"[*] Auditing type-attributed C# surface under {args.source_root}...")
            surface = extract_csharp_surface(args.source_root)
            if args.demo_root and os.path.isdir(args.demo_root):
                print(f"[*] Auditing demo surface under {args.demo_root}...")
                demo_surface = extract_csharp_surface(args.demo_root)
                for type_name, record in demo_surface['types'].items():
                    dest = surface['types'].setdefault(type_name, {'methods': {}, 'members': set(), 'kinds': set()})
                    dest['kinds'].update(record['kinds'])
                    dest['members'].update(record['members'])
                    for m_name, m_info in record['methods'].items():
                        m_dest = dest['methods'].setdefault(m_name, {'count': 0, 'public': False})
                        m_dest['count'] += m_info['count']
                        m_dest['public'] = m_dest['public'] or m_info['public']
                for enum_name, enum_members in demo_surface['enums'].items():
                    surface['enums'].setdefault(enum_name, {}).update(enum_members)
            coverage = compute_coverage(manifest, surface, args.source_root, args.assembly)
            functions = coverage["functions"]
            free = functions['bindings']['free_function']
            member = functions['bindings']['member_function']
            print(
                "[+] Measured coverage: "
                f"free bindings {free[1]}/{free[0]}, "
                f"member bindings {member[1]}/{member[0]}, "
                f"enums {coverage['enums']['present_types']}/{coverage['enums']['canonical_types']}, "
                f"structs {coverage['structs']['present_types']}/{coverage['structs']['canonical_types']}"
            )
            print(
                "[+] Function disposition: "
                f"{functions['implemented']}/{functions['canonical_names']} names implemented "
                f"({functions['fully_implemented']} fully, {len(functions['partial'])} partial, "
                f"{functions['public_exposed']} with a public member), "
                f"{len(functions['misplaced'])} misplaced, "
                f"{len(functions['missing'])} missing, "
                f"{functions['constructors_excluded']} canonical constructors excluded"
            )
            if args.json:
                os.makedirs(os.path.dirname(os.path.abspath(args.json)), exist_ok=True)
                with open(args.json, "w", encoding="utf-8") as f:
                    json.dump(coverage, f, indent=2)
                print(f"[+] Coverage JSON written to {args.json}")

            print_coverage_report(coverage)

        print(f"[OK] Parity extraction completed successfully.")
    except Exception as ex:
        print(f"[ERROR] Failed to extract symbols: {ex}", file=sys.stderr)
        sys.exit(1)


if __name__ == '__main__':
    main()
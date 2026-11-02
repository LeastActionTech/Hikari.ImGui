#!/usr/bin/env python3
"""Regression tests for the type-attributed ImGui parity verifier.

The tests exercise the deterministic parsing and coverage helpers with synthetic inputs so
they do not depend on the canonical checkout or on the state of the C# port.
"""

import json
import sys
import tempfile
import unittest
from pathlib import Path


TOOLS_DIRECTORY = Path(__file__).resolve().parent
if str(TOOLS_DIRECTORY) not in sys.path:
    sys.path.insert(0, str(TOOLS_DIRECTORY))

from verify_imgui_parity import (  # noqa: E402
    compute_coverage,
    evaluate_constant,
    extract_csharp_surface,
    owner_at_position,
    scan_csharp_types,
)


def _function(name, owner, category="General", kind="free_function"):
    """Build one canonical function overload entry for a synthetic manifest."""
    return [{"name": name, "return_type": "void", "parameters": "", "category": category,
             "header": "imgui.h", "owner": owner, "kind": kind}]


def _manifest(functions, enums=None, structs=None, struct_fields=None):
    """Build a synthetic canonical manifest with the keys the verifier consumes."""
    return {
        "metadata": {"counts": {}},
        "functions": functions,
        "enums": enums or {},
        "structs": structs or {},
        "struct_fields": struct_fields or {},
    }


def _type(methods=None, members=None, kinds=None):
    """Build a synthetic C# type record: name to accessibilities is not needed, only publicness."""
    return {
        "methods": methods or {},
        "members": set(members or ()),
        "kinds": set(kinds or ("class",)),
    }


def _method(public=True, count=1):
    """Build a synthetic C# method record."""
    return {"count": count, "public": public}


class ConstantEvaluationTests(unittest.TestCase):
    """Verifies evaluation of the integer expressions used as enum member values."""

    def test_evaluates_shifts_and_hexadecimal(self) -> None:
        """Reads canonical flag expressions such as `1 << 4` and `0x10`."""
        self.assertEqual(evaluate_constant("1 << 4", {}), 16)
        self.assertEqual(evaluate_constant("0x10", {}), 16)

    def test_substitutes_previously_resolved_members(self) -> None:
        """Resolves a member that refers to another member declared earlier."""
        self.assertEqual(evaluate_constant("Base | 1", {"Base": 4}), 5)

    def test_evaluates_unchecked_cast_and_bitwise_not(self) -> None:
        """Reads C# style casts and the canonical `~0x7FFFFFF0` mask.

        The evaluated value may be signed or unsigned depending on how the expression is
        written, so the test compares 32-bit patterns, which is how coverage compares them.
        """
        self.assertEqual(evaluate_constant("unchecked((int)0x8000000F)", {}) & 0xFFFFFFFF, 0x8000000F)
        self.assertEqual(evaluate_constant("~0x7FFFFFF0", {}) & 0xFFFFFFFF, 0x8000000F)

    def test_returns_none_for_an_unresolvable_expression(self) -> None:
        """Returns None rather than a wrong value when a macro cannot be evaluated."""
        self.assertIsNone(evaluate_constant("IMGUI_SOME_MACRO(1)", {}))
        self.assertIsNone(evaluate_constant(None, {}))


class OwnerAttributionTests(unittest.TestCase):
    """Verifies innermost-owner resolution for C++ declarations."""

    def test_returns_innermost_enclosing_type(self) -> None:
        """Prefers the narrowest type body containing the position."""
        ranges = [("Outer", 0, 100), ("Inner", 20, 60)]
        self.assertEqual(owner_at_position(ranges, 30), "Inner")
        self.assertEqual(owner_at_position(ranges, 80), "Outer")
        self.assertIsNone(owner_at_position(ranges, 200))


class CSharpScanTests(unittest.TestCase):
    """Verifies type attribution for the C# surface scan."""

    def test_finds_nested_type_declarations(self) -> None:
        """Reports outer and nested type bodies as separate declarations."""
        text = "public sealed class Outer { private sealed class Inner { } public void Keep() { } }"
        found = scan_csharp_types(text)
        self.assertEqual({entry['name'] for entry in found}, {"Outer", "Inner"})

    def test_extracts_partial_and_internal_members(self) -> None:
        """Merges partial declarations and records internal members as implementations."""
        with tempfile.TemporaryDirectory() as directory:
            Path(directory, "a.cs").write_text(
                "public static partial class Api { public static void Shared() { } }\n")
            Path(directory, "b.cs").write_text(
                "public static partial class Api { internal static void Hidden() { } "
                "private static void Secret() { } }\n")
            surface = extract_csharp_surface(directory)
        methods = surface['types']['Api']['methods']
        self.assertIn('Shared', methods)
        self.assertIn('Hidden', methods)
        self.assertTrue(methods['Shared']['public'])
        self.assertFalse(methods['Hidden']['public'])
        self.assertNotIn('Secret', methods)

    def test_parses_nullable_array_and_generic_members(self) -> None:
        """Reads data members whose types carry nullable or generic suffixes."""
        with tempfile.TemporaryDirectory() as directory:
            Path(directory, "a.cs").write_text(
                "public sealed class Holder { "
                "public byte[]? Bytes { get; set; } "
                "public Dictionary<uint, string>? Map { get; set; } "
                "public float Plain; }\n")
            surface = extract_csharp_surface(directory)
        members = surface['types']['Holder']['members']
        self.assertEqual(members, {"Bytes", "Map", "Plain"})

    def test_skips_build_output_directories(self) -> None:
        """Ignores bin and obj so stale artifacts cannot inflate the surface."""
        with tempfile.TemporaryDirectory() as directory:
            Path(directory, "obj").mkdir()
            Path(directory, "obj", "generated.cs").write_text(
                "public static class Ghost { public static void Phantom() { } }\n")
            surface = extract_csharp_surface(directory)
        self.assertNotIn('Ghost', surface['types'])


class CoverageTests(unittest.TestCase):
    """Verifies the (symbol, owner) binding model used to categorize parity."""

    def test_counts_only_the_owning_type(self) -> None:
        """Treats a method on the owning type as implemented."""
        surface = {'types': {'ImGui': _type({'Begin': _method()})}, 'enums': {}}
        coverage = compute_coverage(_manifest({'Begin': _function('Begin', 'ImGui')}),
                                    surface, 'src', None)
        self.assertEqual(coverage['functions']['implemented'], 1)
        self.assertEqual(coverage['functions']['public_exposed'], 1)

    def test_counts_internal_members_but_not_as_public(self) -> None:
        """Counts an internal implementation while reporting it as not publicly exposed."""
        surface = {'types': {'ImGui': _type({'ItemAdd': _method(public=False)})}, 'enums': {}}
        coverage = compute_coverage(_manifest({'ItemAdd': _function('ItemAdd', 'ImGui')}),
                                    surface, 'src', None)
        self.assertEqual(coverage['functions']['implemented'], 1)
        self.assertEqual(coverage['functions']['public_exposed'], 0)

    def test_reports_a_wrong_type_as_misplaced(self) -> None:
        """Reports a name that exists only on a non-owning type as misplaced, not covered."""
        surface = {'types': {'ImGui': _type(), 'ImFont': _type({'Decode': _method()})}, 'enums': {}}
        coverage = compute_coverage(_manifest({'Decode': _function('Decode', 'ImGui')}),
                                    surface, 'src', None)
        self.assertEqual(coverage['functions']['implemented'], 0)
        self.assertEqual([entry['name'] for entry in coverage['functions']['misplaced']], ['Decode'])

    def test_reports_an_absent_owner_type_as_missing(self) -> None:
        """Reports a missing symbol when no canonical owning type exists, even if the name does."""
        surface = {'types': {'ImRect': _type({'Contains': _method()})}, 'enums': {}}
        coverage = compute_coverage(
            _manifest({'Contains': _function('Contains', 'ImGuiSelectionBasicStorage')}),
            surface, 'src', None)
        self.assertEqual(coverage['functions']['implemented'], 0)
        self.assertEqual(len(coverage['functions']['missing']), 1)
        self.assertEqual(coverage['functions']['missing'][0]['collision'], ['ImRect'])

    def test_tracks_multi_owner_names_per_binding(self) -> None:
        """Requires every owning type to be matched before a name counts as fully implemented."""
        overloads = _function('PushClipRect', 'ImGui') + _function('PushClipRect', 'ImDrawList',
                                                                   kind='member_function')
        partial_surface = {'types': {'ImGui': _type({'PushClipRect': _method()})}, 'enums': {}}
        coverage = compute_coverage(_manifest({'PushClipRect': overloads}), partial_surface, 'src', None)
        self.assertEqual(coverage['functions']['fully_implemented'], 0)
        self.assertEqual(coverage['functions']['partial'][0]['missing_owners'], ['ImDrawList'])
        self.assertEqual(coverage['functions']['implemented'], 1)

        full_surface = {'types': {'ImGui': _type({'PushClipRect': _method()}),
                                  'ImDrawList': _type({'PushClipRect': _method()})}, 'enums': {}}
        coverage = compute_coverage(_manifest({'PushClipRect': overloads}), full_surface, 'src', None)
        self.assertEqual(coverage['functions']['fully_implemented'], 1)
        self.assertEqual(coverage['functions']['partial'], [])

    def test_excludes_canonical_constructors(self) -> None:
        """Excludes constructor declarations from the function denominator."""
        constructor = [{"name": "ImGuiIO", "return_type": "", "parameters": "", "category": "General",
                        "header": "imgui.h", "owner": "ImGuiIO", "kind": "constructor"}]
        coverage = compute_coverage(_manifest({'ImGuiIO': constructor}), {'types': {}, 'enums': {}},
                                    'src', None)
        self.assertEqual(coverage['functions']['constructors_excluded'], 1)
        self.assertEqual(coverage['functions']['canonical_names'], 0)

    def test_flags_enum_value_mismatch_but_not_signedness(self) -> None:
        """Compares enum member values, tolerating a signed/unsigned representation difference."""
        manifest = _manifest({}, enums={'E': [{"name": "A", "value": "1"},
                                              {"name": "B", "value": "1 << 1"},
                                              {"name": "C", "value": "~0"}]})
        surface = {'types': {}, 'enums': {'E': {'A': 1, 'B': 99, 'C': (1 << 32) - 1}}}
        coverage = compute_coverage(manifest, surface, 'src', None)
        mismatches = coverage['enums']['value_mismatches']
        self.assertEqual([entry['member'] for entry in mismatches], ['B'])
        self.assertEqual(coverage['enums']['missing_members'], [])

    def test_reports_enum_members_of_an_absent_type(self) -> None:
        """Lists every member of an enum whose type is not present."""
        manifest = _manifest({}, enums={'E': [{"name": "A", "value": "0"}]})
        coverage = compute_coverage(manifest, {'types': {}, 'enums': {}}, 'src', None)
        self.assertTrue(coverage['enums']['missing_members'][0]['type_absent'])

    def test_matches_struct_fields_case_insensitively(self) -> None:
        """Matches PascalCase C# members against lowercase canonical field names."""
        manifest = _manifest({}, structs={'S': []}, struct_fields={'S': ['pos', '_flags']})
        surface = {'types': {'S': _type(members={'Pos', 'Flags'})}, 'enums': {}}
        coverage = compute_coverage(manifest, surface, 'src', None)
        self.assertEqual(coverage['structs']['field_gaps'], [])
        self.assertEqual(coverage['structs']['fields_checked'], 2)

    def test_reports_a_missing_struct_field(self) -> None:
        """Reports a canonical field that has no C# member."""
        manifest = _manifest({}, structs={'S': []}, struct_fields={'S': ['pos', 'size']})
        surface = {'types': {'S': _type(members={'Pos'})}, 'enums': {}}
        coverage = compute_coverage(manifest, surface, 'src', None)
        self.assertEqual(coverage['structs']['field_gaps'][0]['fields'], ['size'])

    def test_reports_a_struct_with_no_csharp_type(self) -> None:
        """Lists a canonical struct that has no C# counterpart."""
        manifest = _manifest({}, structs={'Missing': []}, struct_fields={'Missing': ['a', 'b']})
        coverage = compute_coverage(manifest, {'types': {}, 'enums': {}}, 'src', None)
        self.assertEqual(coverage['structs']['absent_types'][0]['name'], 'Missing')
        self.assertEqual(coverage['structs']['canonical_types'], 1)
        self.assertEqual(coverage['structs']['present_types'], 0)

    def test_reports_overload_gaps_for_the_owning_type(self) -> None:
        """Counts C# overloads on the owning type against the canonical overload count."""
        overloads = _function('SetNextWindowSize', 'ImGui')
        overloads += [dict(entry, parameters="const ImVec2&, ImGuiCond") for entry in overloads]
        surface = {'types': {'ImGui': _type({'SetNextWindowSize': _method(count=1)})}, 'enums': {}}
        coverage = compute_coverage(_manifest({'SetNextWindowSize': overloads}), surface, 'src', None)
        self.assertEqual(coverage['functions']['overload_gaps'][0]['canonical'], 2)
        self.assertEqual(coverage['functions']['overload_gaps'][0]['csharp'], 1)

    def test_matches_demo_category_on_imgui_demo_class(self) -> None:
        """Allows demo functions to be realized on ImGuiDemo in the demo project."""
        manifest = _manifest({'ShowDemoWindow': _function('ShowDemoWindow', 'ImGui', category='Demo')})
        surface = {'types': {'ImGuiDemo': _type({'ShowDemoWindow': _method(public=True)})}, 'enums': {}}
        coverage = compute_coverage(manifest, surface, 'src', None)
        self.assertEqual(coverage['functions']['implemented'], 1)
        self.assertEqual(coverage['functions']['fully_implemented'], 1)
        self.assertEqual(coverage['categories']['Demo']['implemented'], 1)


if __name__ == "__main__":
    unittest.main()
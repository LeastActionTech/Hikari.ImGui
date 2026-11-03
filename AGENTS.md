# Agent Guidelines for Hikari.ImGui

This document defines the mandatory operating guidelines, coding standards, documentation policies, and testing workflows required of any AI agent (or automated contributor) working within the `Hikari.ImGui` repository.

All agents MUST adhere strictly to the directives outlined in this file.

---

## 1. Mandatory XML Documentation Policy

### 1.1 Universal Coverage Across All Access Modifiers
Every data structure and member declared in this codebase **MUST** be fully documented with C# XML documentation comments (`///`), regardless of its access modifier:
- **Data Structures**: `class`, `struct`, `record`, `interface`, `enum`, `delegate` (whether `public`, `internal`, `protected`, `private`, or `file`-scoped).
- **Members**: Methods, constructors, finalizers, properties, indexers, events, fields, and constants (whether `public`, `internal`, `protected`, `protected internal`, `private protected`, `private`, or `file`-scoped).

> [!IMPORTANT]
> **No exceptions for visibility**: Private helpers, internal data structures, and nested types require the exact same depth and quality of XML documentation as public APIs. Never omit XML comments for private or internal code.

### 1.2 Synchronous Documentation Maintenance
- When introducing **new functionality**, documentation must be authored alongside the implementation.
- When **modifying, refactoring, or extending** existing code:
  - All corresponding XML comments must be audited and updated synchronously in the same change.
  - Parameter tags (`<param>`), return value tags (`<returns>`), and type parameter tags (`<typeparam>`) must match updated signatures exactly.
  - Changes in assumptions, valid input ranges, thread-safety, exceptions thrown, or lifecycle side effects must be documented in `<remarks>` or `<exception>`.
  - Stale, misleading, or outdated comments are considered defect bugs.

---

## 2. MSDN-Style Documentation Standards & Conventions

All XML comments must follow **Microsoft Developer Network (MSDN) / Microsoft Learn** documentation conventions, using clear, precise, and intuitive terminology.

### 2.1 Standard XML Tags
Always employ appropriate XML documentation tags:

| Tag | Purpose & Usage Guidelines |
| :--- | :--- |
| `<summary>` | Concise description of the type or member's primary purpose. Written in active third-person singular present tense. |
| `<param name="...">` | Clear description of each parameter, its purpose, valid input ranges, coordinate spaces, and nullability. |
| `<typeparam name="...">` | Clear description of generic type parameters and constraints. |
| `<returns>` | Exact description of the return value, including special sentinel values (e.g., `-1`, `null`, `false`). |
| `<value>` | Used on properties to explain the value represented by the property. |
| `<exception cref="...">` | Explicit conditions under which the given exception type is thrown. |
| `<remarks>` | In-depth architectural context, subsystem relationships, canonical Dear ImGui reference mapping, algorithmic complexity, performance/allocation considerations, or lifecycle notes. |
| `<example>` | Code snippets or usage demonstrations for complex or foundational APIs. |
| `<see cref="..." />` | Inline hyperlink references to code symbols (types, methods, properties). |
| `<see langword="..." />` | Inline references to language keywords (e.g., `<see langword="null"/>`, `<see langword="true"/>`, `<see langword="false"/>`). |
| `<c>...</c>` | Inline code terms, variable names, or canonical C++ identifiers. |
| `<para>` | Paragraph breaks within multi-paragraph `<summary>` or `<remarks>` blocks. |

### 2.2 Phrasing Rules & Intuitive Terminology

#### Methods
- Begin with a third-person singular active verb (e.g., *Gets*, *Sets*, *Computes*, *Determines*, *Initializes*, *Registers*, *Flattens*, *Tessellates*, *Attempts to*).
- **Avoid conversational filler**: Do NOT use phrases like "This method will calculate..." or "Helper function to...".
- **Intuitive terminology**: Use domain-accurate and intuitive names (e.g., "screen space coordinates", "clipping rectangle", "active ID stack") rather than cryptic abbreviations.

```csharp
/// <summary>
/// Calculates the bounding rectangle of a text string when rendered with the specified font and size.
/// </summary>
/// <param name="font">The font used to measure glyph metrics. Cannot be <see langword="null"/>.</param>
/// <param name="text">The UTF-16 character span to measure.</param>
/// <param name="wrapWidth">The optional width threshold at which lines wrap to the next line. Set to <c>-1.0f</c> to disable wrapping.</param>
/// <returns>An <see cref="ImVec2"/> containing the width and height of the measured text boundary.</returns>
/// <exception cref="ArgumentNullException"><paramref name="font"/> is <see langword="null"/>.</exception>
private static ImVec2 CalcTextSizeInternal(ImFont font, ReadOnlySpan<char> text, float wrapWidth)
```

#### Properties
- **Boolean properties**: Always phrase as `"Gets a value indicating whether..."` (or `"Gets or sets a value indicating whether..."`).
- **Read-write properties**: Phrase as `"Gets or sets..."`.
- **Read-only properties**: Phrase as `"Gets..."`.

```csharp
/// <summary>
/// Gets a value indicating whether the current window is collapsed.
/// </summary>
/// <value>
/// <see langword="true"/> if the window is currently collapsed into its title bar; otherwise, <see langword="false"/>.
/// </value>
internal bool IsCollapsed { get; set; }

/// <summary>
/// Gets the total number of items submitted to the current layout container.
/// </summary>
public int ItemCount { get; }
```

#### Constructors
- Phrase as `"Initializes a new instance of the <see cref=\"T\"/> class."` or `"...struct."`.

```csharp
/// <summary>
/// Initializes a new instance of the <see cref="ImDrawListSharedData"/> class with default rasterizer settings.
/// </summary>
public ImDrawListSharedData()
```

#### Return Values
- Describe both the success and fallback/failure conditions clearly:
  - `<returns><see langword="true"/> if the item was activated; otherwise, <see langword="false"/>.</returns>`
  - `<returns>The index of the active column, or <c>-1</c> if no column is active.</returns>`

#### Canonical Dear ImGui References
Because `Hikari.ImGui` is a pure C# re-implementation of Dear ImGui canonical Dear ImGui (docking), include canonical references in `<remarks>` when porting or modifying canonical behaviors:

```csharp
/// <remarks>
/// Mirrors canonical Dear ImGui <c>ImGui::ButtonBehavior</c> in <c>imgui_widgets.cpp</c>.
/// Handles hover delay timers, repeat rates, and navigation activation routing.
/// </remarks>
```

---

## 3. Testing Mandates & Quality Assurance

### 3.1 Tests Must Pass
- **Zero Regressions**: Any modification to existing code must not break any existing tests.
- **Run the Suite**: Agents must verify that the test suite passes before concluding their work:
  ```bash
  dotnet test Hikari.ImGui.Test -c Release
  ```
  Ensure all unit tests pass with zero failures and zero errors.

### 3.2 Mandatory Test Creation for New Functionality
- **Always Add Tests**: Whenever new functionality, APIs, internal mechanisms, or bug fixes are introduced:
  - You **MUST** author corresponding unit tests in `Hikari.ImGui.Test`.
  - Add test methods in existing test classes (e.g., `WidgetTests.cs`, `DrawTests.cs`, `TableTests.cs`, `ContextTests.cs`) or create a dedicated test fixture if introducing an isolated subsystem.
- **Test Scenarios to Cover**:
  - **Happy Path**: Standard valid input and typical frame execution flow.
  - **Boundary & Edge Conditions**: Zero, negative, extreme values, empty collections/spans, clipping rect overflows, null references where applicable.
  - **State Transitions**: Pushing/popping state stacks (ID stack, style stack, font stack), verifying balanced cleanup.
  - **Regression Verification**: If fixing a bug, add a regression test that fails before the fix and passes after.

### 3.3 Test Design Patterns in Hikari.ImGui
- Tests use **NUnit 3** and target `.NET 10`.
- Extend [`ImGuiTestFixture`](file:///home/akshaym/Documents/Code/Hikari.ImGui/Hikari.ImGui.Test/ImGuiTestFixture.cs) for tests that require a live `ImGuiContext`:
  - `ImGuiTestFixture` handles context creation, teardown isolation, frame simulation, and canonical guard assertion checks (`ExpectAssert`).
  - Use `SimulateFrame(...)` or custom frame loops to verify immediate-mode UI state across multiple frames.

---

## 4. Architectural Directives

- **Zero Native Dependencies**: Maintain 100% pure managed C# code. Do not introduce P/Invoke or native library bindings into `Hikari.ImGui`.
- **Zero Allocations in Render Paths**: Immediate-mode GUI loops execute at high frame rates (60–240+ FPS). Avoid per-frame heap allocations (`new`, closures, boxing, string concatenation) inside critical layout and draw paths.
- **Canonical Parity Compliance**: Respect the layout, algorithms, and symbol contracts defined in `PARITY_COMPLIANCE.md` and `Tools/verify_imgui_parity.py`.

---

## 5. Agent Pre-Submission Checklist

Before finalizing any task or presenting code to the user, verify that:
1. [ ] **XML Documentation**: Every newly added or modified type, method, property, field, and constant has up-to-date, MSDN-style XML comments.
2. [ ] **Access Modifiers**: Private and internal members are documented with the same rigor as public APIs.
3. [ ] **Tests Added**: New tests have been created in `Hikari.ImGui.Test` covering the new behavior, bug fix, or edge cases.
4. [ ] **Tests Pass**: `dotnet test Hikari.ImGui.Test -c Release` executes and completes with 100% passing tests.
5. [ ] **Zero Warnings**: No new compiler warnings or documentation warnings are introduced.
